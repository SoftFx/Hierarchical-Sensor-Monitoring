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

        // Bounded retry of failed loads (#1344), capped exponential backoff: 1h, then 2h, 4h, 8h,
        // 16h, settling at one attempt per sensor per day. The first delay has to outlast the
        // eager CheckSensorsHistoryAsync pass that runs one await before the sweep, so a failure
        // that pass just observed is never retried back-to-back against a still-broken database.
        // Rationale and costs: aicontext/features/server/overview.md.
        private static readonly TimeSpan HistoryLoadFirstRetryDelay = TimeSpan.FromHours(1);

        private static readonly TimeSpan HistoryLoadMaxRetryInterval = TimeSpan.FromHours(24);

        // Last backoff rung: 1h doubled five times is 32h, already past the cap. The retry
        // counter is clamped here, so the doubling below cannot run away.
        private const int MaxRetryBackoffRung = 5;

        // Ticks of the last load attempt, successful or not (0 = never); guarded by _lock.
        // The first retry is measured from this stamp — from the failure itself.
        private long _lastLoadAttemptTicks;

        // Ticks of the last retry attempt (0 = never retried); guarded by _lock. Later
        // retries are measured from this stamp.
        private long _lastLoadRetryTicks;

        // Retries performed so far; guarded by _lock. Drives the backoff step above.
        private int _loadRetryCount;

        // Set by a successful retry, cleared by the first value ingestion stores. Cleared at the
        // write rather than tested against HasData on read, so a later retention pass or history
        // clear cannot resurrect the degraded state of a sensor that recovered long ago.
        private volatile bool _historyRestoredByRetry;

        // _historyLoaded alone would suffice here (it is written only inside Initialize's try,
        // strictly before the latch); the _isInitialized term documents the publication contract
        // rather than adding a state combination that can occur.
        internal override bool IsHistoryLoaded => _isInitialized && _historyLoaded;

        internal override bool HistoryLoadFailed => _isInitialized && !_historyLoaded;

        internal override bool HistoryRestoredByRetry => _historyRestoredByRetry;

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
                // Not a real value: a marker leaves the cache empty, so the sensor stays
                // degraded (see _historyRestoredByRetry).
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

                    _historyRestoredByRetry = false;

                    ReceivedNewValue?.Invoke(validatedValue);
                }
                else
                {
                    // Aggregated into the cached value: the cache is populated either way.
                    _historyRestoredByRetry = false;
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
        // The outcome is reported, not just "did something happen": the sweep budgets its
        // per-sweep cap on Failed alone, so a sensor waiting out its backoff cannot consume a
        // slot another sensor's first retry needs, and recovery is not throttled either.
        internal override HistoryLoadRetryResult RetryFailedHistoryLoad(DateTime utcNow)
        {
            if (!HistoryLoadFailed)
                return HistoryLoadRetryResult.Suppressed;

            lock (_lock)
            {
                if (!HistoryLoadFailed)
                    return HistoryLoadRetryResult.Suppressed;

                // Two clocks. The FIRST retry is measured from the failed load itself and only
                // has to clear one sweep period: a failure the current tick just observed — the
                // eager Initialize() of CheckSensorsHistoryAsync seconds ago, or this sweep's own
                // Initialize() — must not be retried back-to-back against a possibly still-broken
                // database, and must not stamp the backoff at the moment of the first failure.
                // Later retries are spaced from the previous retry by the growing delay.
                var retriedBefore = _loadRetryCount != 0;
                var since = utcNow.Ticks - (retriedBefore ? _lastLoadRetryTicks : _lastLoadAttemptTicks);
                var delay = retriedBefore ? NextRetryDelay(_loadRetryCount) : HistoryLoadFirstRetryDelay;

                // A stamp in the future (negative delta) is not "overdue": it means the clock
                // that stamped it ran ahead of this caller's — a backwards system clock step,
                // or a caller passing a snapshot taken before the load failed. Firing here
                // would hit a possibly still-broken database back-to-back and spend a backoff
                // rung, so re-anchor to the caller's clock and wait the delay out from there;
                // re-anchoring is what keeps a large backwards step from suppressing retries
                // until the wall clock catches up.
                if (since < 0)
                {
                    if (retriedBefore)
                        _lastLoadRetryTicks = utcNow.Ticks;
                    else
                        _lastLoadAttemptTicks = utcNow.Ticks;

                    return HistoryLoadRetryResult.Suppressed;
                }

                if (since < delay.Ticks)
                    return HistoryLoadRetryResult.Suppressed;

                _lastLoadRetryTicks = utcNow.Ticks;

                // Clamped at the last rung: the delay is capped there anyway, and a counter that
                // stopped growing cannot overflow on a sensor that has been failing for years.
                if (_loadRetryCount < MaxRetryBackoffRung)
                    _loadRetryCount++;

                // What enables the rerun is LoadHistoryUnderLock itself — it bypasses
                // Initialize()'s _isInitialized gate. _isInitialized deliberately stays set:
                // clearing it would let a reentrant TryAddValue (the SensorExpired ->
                // SetExpiredSnapshot path) re-enter Initialize() on a now-populated Storage,
                // re-opening the #1296 hazard the Monitor.IsEntered guard above only warns
                // about. _historyLoaded needs no assignment here: HistoryLoadFailed being true
                // already implies it is false, and LoadHistoryUnderLock sets it on completion.
                LoadHistoryUnderLock(isRetry: true, utcNow);

                return HistoryLoadFailed ? HistoryLoadRetryResult.Failed : HistoryLoadRetryResult.Loaded;
            }
        }

        // Delay before the retry that follows retryCount retries: 2h, 4h, 8h, 16h, then the cap.
        private static TimeSpan NextRetryDelay(int retryCount) =>
            TimeSpan.FromTicks(Math.Min(HistoryLoadFirstRetryDelay.Ticks * (1L << retryCount), HistoryLoadMaxRetryInterval.Ticks));

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
        // ingestion". What the two modes may write differs because ingestion is blocked for one
        // and not the other:
        //
        // - Cold load: TryAddValue parks in Initialize() on this same lock, so this is the only
        //   mode that may rebuild Storage and run the policy fan-out.
        // - Retry: _isInitialized stays set, so ingestion runs lock-free against a ValuesStorage
        //   that is not safe for concurrent writes. Its ONLY writes are SetLastActivity (single
        //   writer, and To maxes it against the ingestion stamp at read time) and Cut below.
        //   Not _lastTimeout, which ingestion mutates with a bare read-compare-write: a second
        //   writer there can lose an update and regress the one signal a marker-only sensor has.
        //   Not a replayed row (torn _lastValue/_to, a duplicated newest cache point), and not
        //   TryValidate, which reaches SensorExpired -> SetExpiredSnapshot -> TryAddValue and
        //   would emit notifications and DB writes from the maintenance sweep.
        //
        // Restoring the floor is not optional: a successful retry latches _historyLoaded, so a
        // retry that restored nothing would let ShouldDestroy() fall through to CreationDate and
        // delete an established sensor whose newest value is minutes old.
        //
        // Residual limits (value cache, LastTimeout, IsExpired and TTL clocks unrestored; the
        // floor stamped without validating the row) are in
        // aicontext/features/server/overview.md (#1344). All of them point the same way: a
        // retried sensor destroys later, never earlier.
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
                _logger.Info(isRetry
                    ? $"Sensor {Id} history restored by retry {From}-{To} (cache not rebuilt)"
                    : $"Sensor {Id} initialized {From}-{To}");
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
