using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.AccessManager.Formatters;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model.Policies;
using NLog;


namespace HSMServer.Core.Model
{
    public abstract class BaseSensorModel<T> : BaseSensorModel where T : BaseValue, new()
    {
        private readonly MemoryPackFormatter _formatter = new MemoryPackFormatter();

        private readonly Logger _logger = LogManager.GetLogger(nameof(BaseSensorModel));

        protected readonly Func<BaseValue> _getLastValue, _getFirstValue;

        public override SensorPolicyCollection<T> Policies { get; }

        protected override ValuesStorage<T> Storage { get; }

        private readonly IDatabaseCore _database;


        // volatile: TryAddValue/TryUpdateLastValue/CheckTimeout read it lock-free, and "true" must
        // never be observable before the history load in Initialize() has finished filling Storage.
        private volatile bool _isInitialized;

        // Set only when the history load completed without throwing. _isInitialized latches on
        // failure too (anti-retry-storm, see Initialize()); IsHistoryLoaded is the stricter
        // "Storage reflects history" predicate that destructive readers key on.
        private volatile bool _historyLoaded;

        private readonly object _lock = new();

        // Bounded retry of failed loads (#1344), capped exponential backoff: 1h, then 2h, 4h,
        // 8h, 16h, settling at one attempt per sensor per day. What the first delay must exceed
        // is the duration of the eager CheckSensorsHistoryAsync pass that runs one await before
        // the sweep, so a failure that pass just observed is never retried back-to-back against
        // a possibly still-broken database. An hour covers that pass on any tree we expect; on
        // one where it runs longer, a sensor that failed at the start of the pass can be retried
        // by the same tick's sweep — benign, since an hour has genuinely elapsed since it failed.
        // (It happens to equal ClearDatabaseService.Delay, but nothing requires that: a longer
        // or shorter sweep period only shifts when the first retry lands.) The cost of a whole
        // hour is that a failure mid-interval waits up to 2h for it. The growth keeps a longer
        // outage from costing the sensor a full day of disabled self-destroy; the cap keeps a
        // permanently broken database at the #1296 anti-storm budget.
        private static readonly TimeSpan HistoryLoadFirstRetryDelay = TimeSpan.FromHours(1);

        private static readonly TimeSpan HistoryLoadMaxRetryInterval = TimeSpan.FromHours(24);

        // Last backoff rung: 1h doubled five times is 32h, already past the cap. The retry
        // counter is clamped here, so the doubling below cannot run away.
        private const int MaxRetryBackoffShift = 5;

        // Ticks of the last load attempt, successful or not (0 = never); guarded by _lock.
        // The first retry is measured from this stamp — from the failure itself.
        private long _lastLoadAttemptTicks;

        // Ticks of the last retry attempt (0 = never retried); guarded by _lock. Later
        // retries are measured from this stamp.
        private long _lastLoadRetryTicks;

        // Retries performed so far; guarded by _lock. Drives the backoff step above.
        private int _loadRetryCount;

        // Set by a successful retry and never cleared: paired with an empty Storage it is what
        // HistoryRestoredByRetry reports, and once ingestion refills the cache the sensor is no
        // longer degraded, so the pair goes quiet on its own.
        private volatile bool _historyRestoredByRetry;

        // _historyLoaded alone would suffice here (it is written only inside Initialize's try,
        // strictly before the latch); the _isInitialized term documents the publication contract
        // rather than adding a state combination that can occur.
        internal override bool IsHistoryLoaded => _isInitialized && _historyLoaded;

        internal override bool HistoryLoadFailed => _isInitialized && !_historyLoaded;

        internal override bool HistoryRestoredByRetry => _historyRestoredByRetry && !Storage.HasData;

        protected BaseSensorModel(SensorEntity entity, IDatabaseCore database) : base(entity) 
        {
            _database = database;

            if (database == null)
            {
                // No database -> nothing to load; Storage is the only source of truth.
                _isInitialized = true;
                _historyLoaded = true;
            }
        }


        internal override void Revalidate()
        {
            if (LastValue is not null)
                Policies.TryRevalidate(LastValue);
        }

        internal override bool TryAddValue(BaseValue value)
        {
            if (!_isInitialized)
                Initialize();

            if (value?.IsTimeout ?? false)
            {
                Storage.AddValueBase((T)value);
                ReceivedNewValue?.Invoke(value);
                return true;
            }

            if (IsSingleton && !Storage.IsNewSingletonValue(value))
                return false;

            if (value is T valueT && Statistics.HasEma())
                value = Storage.CalculateStatistics(valueT);

            bool isLastValue = Storage.LastValue is null || value.Time >= Storage.LastValue.Time;
            bool canStore = Policies.TryValidate(value, out var validatedValue, isLastValue);

            if (canStore)
            {
                bool isNewValue = !AggregateValues || !Storage.TryAggregateValue(validatedValue);

                if (isNewValue)
                {
                    if (!AggregateValues)
                        Storage.AddValue(validatedValue);

                    ReceivedNewValue?.Invoke(validatedValue);
                }
            }

            return canStore;
        }

        internal override bool TryUpdateLastValue(BaseValue value)
        {
            if (!_isInitialized)
                Initialize();

            if (Statistics.HasEma() && value is T valueT)
                value = Storage.RecalculateStatistics(valueT);

            if (!Storage.TryChangeLastValue(value) || !Policies.TryRevalidate(value))
                return false;

            ReceivedNewValue?.Invoke(value);

            return true;
        }


        internal override bool CheckTimeout()
        {
            if (!_isInitialized)
                Initialize();

            return Policies.SensorTimeout(LastValue);
        }

        internal override IEnumerable<BaseValue> Convert(List<byte[]> pages) => pages.Select(Convert);

        internal override BaseValue Convert(byte[] bytes) => _formatter.Deserialize(bytes);

        internal override BaseValue ConvertFromJson(string data) => JsonSerializer.Deserialize<T>(data);

        internal override BaseValue GetEmptyValue() => new T();

        internal override void Initialize()
        {
            if (_isInitialized)
                return;

            // Monitor is reentrant, and _isInitialized is deliberately still false while a cold
            // load runs, so a same-thread re-entry would replay the whole history load: the
            // TryValidate calls below can reach SensorTimeout -> SensorExpired -> TryAddValue ->
            // Initialize. Returning here stops the replay but leaves that nested caller running
            // against a half-built Storage — #1296 on the initializing thread. On the cold path
            // this is unreachable except by an ordering accident (HasData is false at both
            // TryValidate sites), so log it: if a policy change ever makes it reachable, this line
            // is the only thing that will say so. The retry path (RetryFailedHistoryLoad) never
            // clears _isInitialized, so it cannot reach this guard.
            if (Monitor.IsEntered(_lock))
            {
                _logger.Warn($"Reentrant Initialize on sensor {Id} during history load — the nested value path sees a partial Storage (#1296)");
                return;
            }

            lock (_lock)
            {
                if (_isInitialized)
                    return;

                LoadHistoryUnderLock(isRetry: false, DateTime.UtcNow);
            }

        }

        // Rerun the history load after a failure (#1344), from the maintenance sweep only — the
        // per-value paths never retry (#1296). The gate is checked and stamped inside the lock
        // so concurrent sweeps cannot double-fire it.
        //
        // Returns true only when the load actually ran, false on every suppressed call: the
        // sweep charges its per-sweep retry budget on the return value, so a sensor waiting out
        // its backoff cannot consume a slot another sensor's first retry needs.
        internal override bool RetryFailedHistoryLoad(DateTime utcNow)
        {
            if (!HistoryLoadFailed)
                return false;

            lock (_lock)
            {
                if (!HistoryLoadFailed)
                    return false;

                // Two clocks. The FIRST retry is measured from the failed load itself and only
                // has to clear one sweep period: a failure the current tick just observed — the
                // eager Initialize() of CheckSensorsHistoryAsync seconds ago, or this sweep's own
                // Initialize() — must not be retried back-to-back against a possibly still-broken
                // database, and must not stamp the backoff at the moment of the first failure.
                // Later retries are spaced from the previous retry by the growing delay.
                var retriedBefore = _loadRetryCount != 0;
                var since = utcNow.Ticks - (retriedBefore ? _lastLoadRetryTicks : _lastLoadAttemptTicks);
                var delay = retriedBefore ? NextRetryDelay(_loadRetryCount) : HistoryLoadFirstRetryDelay;

                // A backwards clock step makes the delta negative — treat anything below zero as
                // "delay elapsed" so a clock correction cannot suppress retries for as long as
                // the step lasts. Accepted side effect: such a step spends one backoff rung.
                if (since >= 0 && since < delay.Ticks)
                    return false;

                _lastLoadRetryTicks = utcNow.Ticks;

                // Clamped at the last rung: the delay is capped there anyway, and a counter that
                // stopped growing cannot overflow on a sensor that has been failing for years.
                if (_loadRetryCount < MaxRetryBackoffShift)
                    _loadRetryCount++;

                // What enables the rerun is LoadHistoryUnderLock itself — it bypasses
                // Initialize()'s _isInitialized gate. _isInitialized deliberately stays set:
                // clearing it would let a reentrant TryAddValue (the SensorExpired ->
                // SetExpiredSnapshot path) re-enter Initialize() on a now-populated Storage,
                // re-opening the #1296 hazard the Monitor.IsEntered guard above only warns
                // about. _historyLoaded needs no assignment here: HistoryLoadFailed being true
                // already implies it is false, and LoadHistoryUnderLock sets it on completion.
                LoadHistoryUnderLock(isRetry: true, utcNow);

                return true;
            }
        }

        // Delay before the retry that follows retryCount retries: 2h, 4h, 8h, 16h, then the cap.
        private static TimeSpan NextRetryDelay(int retryCount)
        {
            var ticks = HistoryLoadFirstRetryDelay.Ticks * (1L << retryCount);

            return ticks >= HistoryLoadMaxRetryInterval.Ticks ? HistoryLoadMaxRetryInterval : TimeSpan.FromTicks(ticks);
        }

        // Must be called with _lock held. Publishes the latch (on success OR failure) via its
        // finally block, so a caller below can never observe an unlatched sensor. utcNow is the
        // single clock source for the retry gates: it stamps the attempt clock below and
        // RetryFailedHistoryLoad compares against that stamp, so both must come from one
        // timeline — an ambient DateTime.UtcNow here would read a back-dated caller clock as a
        // backwards step and fire the retry immediately.
        //
        // isRetry is a parameter, not something inferred from Storage: a live sensor can have a
        // null LastValue (timeout-only traffic, values rejected by validation, a retention purge
        // that emptied the cache mid-tick), so "Storage looks empty" does not mean "no live
        // ingestion". The modes differ in what they may write:
        //
        // - Cold load (isRetry=false): ingestion is blocked — TryAddValue parks in Initialize()
        //   on this same lock — so this is the only mode that may rebuild Storage and run the
        //   policy fan-out.
        // - Retry (isRetry=true): _isInitialized stays set, so ingestion runs lock-free against
        //   a ValuesStorage that is not safe for concurrent writes. A replayed DB row would race
        //   it (torn _lastValue/_to, a duplicated newest point in the cache), and TryValidate
        //   would reach SensorExpired -> SetExpiredSnapshot -> TryAddValue, emitting
        //   notifications and DB writes from the maintenance sweep and forcing IsExpired from a
        //   stale marker onto a possibly-reporting sensor. So a retry writes only the activity
        //   signal ShouldDestroy() reads, plus Cut below.
        //
        // Restoring that signal is not optional: a successful retry latches _historyLoaded, so a
        // retry that restored nothing would let ShouldDestroy() fall through to CreationDate and
        // delete an established sensor whose newest value is minutes old.
        //
        // The signal is always SetLastActivity, for a marker row as much as for a value row.
        // Restoring _lastTimeout instead would put a second writer on a field ingestion mutates
        // lock-free, and its bare read-compare-write can lose an update: a sensor fed only
        // timeout markers could have its LastTimeout regress to the DB row and be destroyed
        // while reporting. _lastActivity has exactly one writer (this method, under _lock) and
        // feeds ShouldDestroy() through To just as well.
        //
        // Limits: the value cache, LastTimeout, IsExpired and the TTL clocks are NOT restored,
        // and the floor is stamped without validating the row (the cold load skips a
        // policy-rejected newest row, so a retried sensor can look active where its
        // cleanly-loaded twin would not — conservative, it destroys later). See
        // aicontext/features/server/overview.md (#1344).
        private void LoadHistoryUnderLock(bool isRetry, DateTime utcNow)
        {
            // Every attempt stamps the clock, failed or not: the first retry is measured from
            // the failure itself, so the sweep that observed the failure cannot fire a
            // back-to-back attempt against a possibly still-broken database.
            _lastLoadAttemptTicks = utcNow.Ticks;

            try
            {
                BaseValue last, first;
                var lastBytes = _database.GetLatestValue(Id, DateTime.MaxValue.Ticks);
                if (lastBytes != null)
                {
                    var firstBytes = _database.GetFirstValue(Id);

                    last = Convert(lastBytes);
                    // Null-guarded because the catch below now latches: a throw here would leave
                    // _isInitialized true over an empty Storage permanently — the #1296 symptom,
                    // no longer bounded to a startup window. Both reads are reachable as null:
                    // retention (KeepHistory) can sweep every real value and leave only the
                    // timeout marker SetExpiredSnapshot wrote as the newest row.
                    first = firstBytes != null ? Convert(firstBytes) : null;

                    if (!isRetry)
                    {
                        if (last.IsTimeout)
                        {
                            var valueBytes = _database.GetLatestValue(Id, last.Time.Ticks - 1);
                            var value = valueBytes != null ? Convert(valueBytes) : null;

                            if (value is not null && !value.IsTimeout &&
                                Policies.TryValidate(value, out _))
                                Storage.AddValue((T)value);

                            IsExpired = true;
                            foreach (var ttl in Policies.TTLPolicies)
                                ttl.InitLastTtlTime(last.Time);

                            Storage.AddValue((T)last);
                        }
                        else if (Policies.TryValidate(last, out _))
                        {
                            Storage.AddValue((T)last);
                        }
                    }
                    else
                    {
                        // Marker .Time, real value .LastUpdateTime: GetTimeoutValue copies
                        // LastReceivingTime from the previous value, so a marker's
                        // LastUpdateTime under-estimates activity — while for a real value it
                        // is what the cold path is judged on, and an aggregated row's
                        // LastReceivingTime can be days newer than its Time.
                        Storage.SetLastActivity(last.IsTimeout ? last.Time : last.LastUpdateTime);
                    }

                    // Both modes: From feeds decisions, not just display — KeepHistory
                    // retention fires on TimeIsUp(From), and only Cut restores the true
                    // oldest-row time after a failed load left From at MinValue. Accepted
                    // race: this bare _from write can interleave with ClearSensorHistory's
                    // Cut(to) (no shared lock); the worst case is the displayed From reverting
                    // past a just-completed clear — display-range only, no data effect.
                    if (first != null)
                        Storage.Cut(first.Time);
                }

                // Before the log line: an NLog throw must not classify a successful load as failed.
                _historyLoaded = true;
                _historyRestoredByRetry = isRetry;
                _logger.Info($"Sensor {Id} initialized {From}-{To}");
            }
            catch (Exception ex)
            {
                // Distinguish the retry: a failing retry means the database is still broken
                // after the first-retry grace and the sensor's history stays unloaded — the
                // actionable case for the sweep's warning line.
                _logger.Error(ex, isRetry
                    ? $"Sensor history load retry failed {Id}"
                    : $"Sensor initialization error {Id}");
            }
            finally
            {
                // Published once the load has finished OR FAILED (#1296): a failed load latches
                // too and publishes an empty Storage, deliberately — one loud error above beats
                // a per-value retry storm against a broken database. Contract and its limits:
                // aicontext/features/server/overview.md, BaseSensorModel<T>. Bounded rerun:
                // RetryFailedHistoryLoad (#1344).
                _isInitialized = true;
            }
        }
    }
}
