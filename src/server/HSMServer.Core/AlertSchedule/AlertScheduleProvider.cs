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

        private readonly object _lock = new object();

        private readonly Timer _cleanupTimer;
        private volatile int _disposed = 0;

        private class CacheEntry
        {
            public AlertSchedule Schedule { get; set; }
            public DateTime? CachedTime { get; set; }
            public bool? WorkingTimeResult { get; set; }

            public Dictionary<CacheEntryKey, bool> IntervalCache { get; set; } = new();


            public bool IsCacheValid(DateTime time)
            {
                if (!CachedTime.HasValue || !WorkingTimeResult.HasValue)
                    return false;

                return RoundToMinute(CachedTime.Value) == RoundToMinute(time);
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
                CachedTime = null;
                WorkingTimeResult = null;
                IntervalCache.Clear();
            }

            private static DateTime RoundToMinute(DateTime time)
            {
                return new DateTime(time.Year, time.Month, time.Day,
                    time.Hour, time.Minute, 0, time.Kind);
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
                    if (cacheEntry.IsCacheValid(time))
                    {
                        return cacheEntry.WorkingTimeResult.Value;
                    }

                    var result = cacheEntry.Schedule.IsWorkingTime(time);

                    cacheEntry.CachedTime = time;
                    cacheEntry.WorkingTimeResult = result;

                    return result;
                }
                else
                {
                    _logger.Error($"Alert Schedule with id = {id} was not found.");
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
                    _logger.Error($"Alert Schedule with id = {id} was not found.");
                    return true;
                }
            }
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

        private void CleanupIntervalCache()
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

                    if (totalRemoved > 0)
                    {
                        _logger.Debug($"Cleaned up {totalRemoved} expired interval cache entries");
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
                        _logger.Error(ex, $"Failed to parse AlertSchedule {new Guid(entity.Id)} ('{entity.Name}'). A stub schedule will be used.");
                        schedule = new AlertSchedule()
                        {
                            Id = new Guid(entity.Id),
                            Name = entity.Name,
                            Timezone = entity.Timezone,
                            Schedule = entity.Schedule,
                        };
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
