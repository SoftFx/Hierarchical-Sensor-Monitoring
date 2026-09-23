using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NLog;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model.Policies;


namespace HSMServer.Core.Schedule
{
    public class AlertScheduleProvider : IAlertScheduleProvider, IDisposable
    {
        private readonly struct CacheEntryKey : IEquatable<CacheEntryKey>
        {
            public DateTime StartTime { get; }
            public DateTime EndTime { get; }

            public CacheEntryKey(DateTime startTime, DateTime endTime)
            {
                StartTime = startTime;
                EndTime = endTime;
            }

            public bool Equals(CacheEntryKey other) =>
                StartTime == other.StartTime && EndTime == other.EndTime;

            public override bool Equals(object obj) => obj is CacheEntryKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(StartTime, EndTime);

            public static bool operator ==(CacheEntryKey left, CacheEntryKey right) => left.Equals(right);
            public static bool operator !=(CacheEntryKey left, CacheEntryKey right) => !left.Equals(right);
        }
        private readonly TimeSpan CLEANUP_PERIOD = TimeSpan.FromMinutes(5);

        private readonly IDatabaseCore _database;
        private readonly AlertScheduleParser _parser = new();
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private readonly Dictionary<Guid, CacheEntry> _cache = new();

        // Dangling schedule ids fail open (#1405) but are REPORTED at most
        // once per id per interval (#1409): the TTL gates call IsWorkingTime
        // per scheduled policy per sweep, so a deleted schedule referenced by
        // a sensor park used to produce one ERROR line per policy per sweep.
        // The interval keeps the problem periodically visible (it can persist
        // for the process lifetime) instead of muting it after the first hit.
        // Timestamps are Environment.TickCount64 (monotonic — a wall-clock
        // backwards jump must not mute the report) and the cleanup timer
        // prunes entries older than twice the report interval.
        private readonly Dictionary<Guid, long> _missingScheduleReportedAt = new();

        private readonly object _lock = new object();

        private readonly Timer _cleanupTimer;
        private volatile int _disposed = 0;

        private class CacheEntry
        {
            // Per-minute result slots instead of a single (time, result) pair:
            // the TTL gate queries at UtcNow while the data-policy gate queries
            // at the value's own timestamp for the same schedule id (#1404), so
            // one overwritten slot thrashed on every ingested value and
            // AlertSchedule.IsWorkingTime ran twice per value under the
            // provider's lock. A few minute-keyed slots cover the hot path's
            // distinct instants; the map stays bounded by EVICTING the oldest
            // inserted minute on overflow — a full wipe would degrade to a
            // wipe-per-miss under a workload rotating more distinct minutes
            // than there are slots (history replay, backfilled values, bar
            // OpenTime/CloseTime spanning minutes) and never hold the hot path.
            private const int MaxCachedMinutes = 4;

            public AlertSchedule Schedule { get; set; }

            private readonly Dictionary<DateTime, bool> _workingTimeByMinute = new();

            // Insertion order of the distinct minutes currently in the map
            // (the eviction victim selector); its length never exceeds
            // MaxCachedMinutes.
            private readonly Queue<DateTime> _minuteInsertionOrder = new();

            public Dictionary<CacheEntryKey, bool> IntervalCache { get; set; } = new();


            public bool TryGetWorkingTime(DateTime time, out bool result) =>
                _workingTimeByMinute.TryGetValue(NormalizeToUtcMinute(time), out result);

            public void AddWorkingTime(DateTime time, bool result)
            {
                var minute = NormalizeToUtcMinute(time);

                // Defensive: unreachable today (called only on a TryGetWorkingTime
                // miss, so the minute is never present), but the guard keeps the
                // queue and the map in lockstep if a second caller ever appears —
                // an unguarded re-add would enqueue a duplicate minute and evict
                // one victim too many.
                if (_workingTimeByMinute.Count >= MaxCachedMinutes && !_workingTimeByMinute.ContainsKey(minute))
                    _workingTimeByMinute.Remove(_minuteInsertionOrder.Dequeue());

                if (_workingTimeByMinute.TryAdd(minute, result))
                    _minuteInsertionOrder.Enqueue(minute);
            }

            public void AddIntervalToCache(DateTime startTime, DateTime endTime, bool result)
            {
                var key = new CacheEntryKey(startTime, endTime);
                IntervalCache[key] = result;
            }

            public bool? GetCachedIntervalResult(DateTime startTime, DateTime endTime)
            {
                var key = new CacheEntryKey(startTime, endTime);
                if (IntervalCache.TryGetValue(key, out var result))
                    return result;
                return null;
            }

            public void InvalidateCache()
            {
                _workingTimeByMinute.Clear();
                _minuteInsertionOrder.Clear();
                IntervalCache.Clear();
            }

            // Dictionary<DateTime, bool> compares ticks and IGNORES Kind, but
            // AlertSchedule.ConvertUtcToLocalTime treats a Local argument as
            // its host-shifted UTC instant while Utc/Unspecified read the
            // ticks as-is — so a Local-ticks-equal argument would inherit the
            // other Kind's cached answer. Normalizing every key to its UTC
            // instant (Kind included) keeps distinct instants in distinct
            // slots; all production callers already pass Utc-kind timestamps,
            // so the Local branch is a guard, not a hot path.
            private static DateTime NormalizeToUtcMinute(DateTime time)
            {
                var utc = time.Kind == DateTimeKind.Local
                    ? time.ToUniversalTime()
                    : DateTime.SpecifyKind(time, DateTimeKind.Utc);

                return new DateTime(utc.Year, utc.Month, utc.Day,
                    utc.Hour, utc.Minute, 0, DateTimeKind.Utc);
            }
        }


        public AlertScheduleProvider(IDatabaseCore database)
        {
            _database = database;
            LoadSchedulesFromDatabase();

            _cleanupTimer = new Timer(_ => CleanupIntervalCache(),
                null,
                CLEANUP_PERIOD,
                CLEANUP_PERIOD);
        }

        public bool IsWorkingTime(Guid id, DateTime time)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(id, out var cacheEntry))
                {
                    if (cacheEntry.TryGetWorkingTime(time, out var result))
                        return result;

                    var computed = cacheEntry.Schedule.IsWorkingTime(time);

                    cacheEntry.AddWorkingTime(time, computed);

                    return computed;
                }
                else
                {
                    ReportMissingSchedule(id);

                    return true;
                }
            }
        }

        public bool IsWorkingTime(Guid id, DateTime startTime, DateTime endTime)
        {
            if (startTime >= endTime)
                throw new ArgumentException("Start time must be less than end time", nameof(startTime));

            lock (_lock)
            {
                if (_cache.TryGetValue(id, out var cacheEntry))
                {
                    var cachedResult = cacheEntry.GetCachedIntervalResult(startTime, endTime);
                    if (cachedResult.HasValue)
                    {
                        return cachedResult.Value;
                    }

                    var result = cacheEntry.Schedule.IsWorkingTime(startTime, endTime);

                    cacheEntry.AddIntervalToCache(startTime, endTime, result);

                    return result;
                }
                else
                {
                    ReportMissingSchedule(id);

                    return true;
                }
            }
        }

        // Time-based throttle for the missing-id report: once per id per
        // interval, re-armed immediately by SaveSchedule. Internal-settable so
        // tests can shrink it to milliseconds.
        internal TimeSpan MissingScheduleReportInterval { get; set; } = TimeSpan.FromHours(1);

        // Test seam for the throttle's monotonic clock (#1409 review round
        // 5): shifts a stored report timestamp into the PAST so tests can
        // expire the report window or stale-date the prune threshold
        // deterministically, instead of Thread.Sleep-ing out wall-clock
        // intervals. Internal; no production callers.
        internal void BackdateMissingScheduleReport(Guid id, long byMilliseconds)
        {
            lock (_lock)
            {
                if (_missingScheduleReportedAt.TryGetValue(id, out var reportedAt))
                    _missingScheduleReportedAt[id] = reportedAt - byMilliseconds;
            }
        }

        // Must be called under _lock only.
        private void ReportMissingSchedule(Guid id)
        {
            // TickCount64, not DateTime.UtcNow: a pure throttle wants a
            // monotonic source, so a backwards wall-clock jump cannot yield
            // a negative delta and mute the report for the jump duration.
            var now = Environment.TickCount64;

            if (_missingScheduleReportedAt.TryGetValue(id, out var reportedAt) &&
                now - reportedAt < MissingScheduleReportInterval.TotalMilliseconds)
                return;

            _missingScheduleReportedAt[id] = now;

            _logger.Error($"Alert Schedule with id = {id} was not found.");
        }

        public void DeleteSchedule(Guid id)
        {
            lock (_lock)
            {
                _cache.Remove(id);
                _database.RemoveAlertSchedule(id);
            }
        }

        public List<AlertSchedule> GetAllSchedules()
        {
            lock (_lock)
            {
                return [.. _cache.Values.Select(x  => x.Schedule).ToList()];
            }
        }

        public AlertSchedule GetSchedule(Guid id)
        {
            lock (_lock)
            {
                _cache.TryGetValue(id, out var entry);
                return entry?.Schedule;
            }
        }

        public void SaveSchedule(AlertSchedule schedule)
        {
            lock (_lock)
            {
                // The id has a live schedule again — a later disappearance is
                // a new event and must be reported afresh, without waiting out
                // the throttle interval (#1409).
                _missingScheduleReportedAt.Remove(schedule.Id);

                if (_cache.TryGetValue(schedule.Id, out var cacheEntry))
                {
                    cacheEntry.Schedule = schedule;
                    cacheEntry.InvalidateCache();
                }
                else
                {
                    _cache[schedule.Id] = new CacheEntry
                    {
                        Schedule = schedule,
                    };
                }

                _database.AddAlertSchedule(schedule.ToEntity());
            }
        }

        // Timer callback. Internal so tests can drive the prune deterministically —
        // AlertScheduleProviderMissingIdLoggingTests calls it directly to pin the
        // missing-id throttle prune; making it private breaks that suite's build.
        //
        // Prunes IntervalCache only. _workingTimeByMinute is DELIBERATELY not
        // pruned here: it is hard-bounded at MaxCachedMinutes (4) slots per
        // schedule entry and self-evicts on overflow, so it needs no periodic
        // cleanup; IntervalCache is the only unbounded structure (one entry
        // per distinct (start, end) argument pair).
        internal void CleanupIntervalCache()
        {
            try
            {
                lock (_lock)
                {
                    var cleanupThreshold = DateTime.UtcNow.AddMinutes(-1);
                    int totalRemoved = 0;

                    foreach (var cacheEntry in _cache.Values)
                    {
                        var keysToRemove = new HashSet<CacheEntryKey>();

                        foreach (var kvp in cacheEntry.IntervalCache)
                        {
                            if (kvp.Key.EndTime < cleanupThreshold)
                            {
                                keysToRemove.Add(kvp.Key);
                            }
                        }

                        foreach (var key in keysToRemove)
                        {
                            cacheEntry.IntervalCache.Remove(key);
                        }

                        totalRemoved += keysToRemove.Count;
                    }

                    // Prune the missing-id throttle timestamps older than
                    // twice the report interval (#1409 review): the growth is
                    // bounded by the number of distinct dangling ids, but the
                    // timer is right here, and a pruned entry re-adds itself
                    // on the next hit.
                    var throttleThreshold = Environment.TickCount64 - (long)(2 * MissingScheduleReportInterval.TotalMilliseconds);
                    var staleThrottleIds = _missingScheduleReportedAt
                        .Where(kvp => kvp.Value < throttleThreshold)
                        .Select(kvp => kvp.Key)
                        .ToList();

                    foreach (var id in staleThrottleIds)
                        _missingScheduleReportedAt.Remove(id);

                    if (totalRemoved > 0 || staleThrottleIds.Count > 0)
                    {
                        _logger.Debug($"Cleaned up {totalRemoved} expired interval cache entries and {staleThrottleIds.Count} stale missing-schedule throttle entries");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during interval cache cleanup");
            }
        }

        private void LoadSchedulesFromDatabase()
        {
            var scheduleEntities = _database.GetAllAlertSchedules();
            lock (_lock)
            {
                foreach (var entity in scheduleEntities)
                {
                    AlertSchedule schedule = null;
                    try
                    {
                        schedule = _parser.Parse(entity);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, $"Failed to parse alert schedule. Id = {entity.Id}, Name = {entity.Name}. Schedule will be skipped.");
                        continue;
                    }

                    _cache[schedule.Id] = new CacheEntry
                    {
                       Schedule = schedule,
                    };
                }
            }
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
                return;

            _cleanupTimer?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
