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

        // Bounded retry of failed loads (#1344): later retries are spaced at least this far
        // apart, so a broken database sees at most one retry attempt per sensor per day,
        // never a per-value storm (#1296).
        private static readonly TimeSpan HistoryLoadRetryInterval = TimeSpan.FromHours(24);

        // The first retry after a failure only has to clear one maintenance-sweep period
        // (ClearDatabaseService runs hourly): the sweep that just saw the failure — including
        // the eager Initialize() CheckSensorsHistoryAsync ran seconds earlier in the same
        // maintenance tick — must not hit the database back-to-back, but a transient outage
        // must not cost the sensor a full day of disabled self-destroy waiting for the 24h
        // gate.
        private static readonly TimeSpan HistoryLoadFirstRetryDelay = TimeSpan.FromHours(1);

        // Ticks of the last load attempt, successful or not (0 = never); guarded by _lock.
        // The first retry is measured from this stamp — from the failure itself.
        private long _lastLoadAttemptTicks;

        // Ticks of the last retry attempt (0 = never retried); guarded by _lock. Later
        // retries are measured from this stamp.
        private long _lastLoadRetryTicks;

        // _historyLoaded alone would suffice here (it is written only inside Initialize's try,
        // strictly before the latch); the _isInitialized term documents the publication contract
        // rather than adding a state combination that can occur.
        internal override bool IsHistoryLoaded => _isInitialized && _historyLoaded;

        internal override bool HistoryLoadFailed => _isInitialized && !_historyLoaded;

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

                LoadHistoryUnderLock(isRetry: false);
            }

        }

        // Rerun the history load after a failure (#1344), from the maintenance sweep only — the
        // per-value paths never retry (#1296). The gate is checked and stamped inside the lock
        // so concurrent sweeps cannot double-fire it.
        internal override void RetryFailedHistoryLoad(DateTime utcNow)
        {
            if (!HistoryLoadFailed)
                return;

            lock (_lock)
            {
                if (!HistoryLoadFailed)
                    return;

                // Two clocks. The FIRST retry is measured from the failed load itself and only
                // has to clear one sweep period: a failure the current tick just observed — the
                // eager Initialize() of CheckSensorsHistoryAsync seconds ago, or this sweep's own
                // Initialize() — must not be retried back-to-back against a possibly still-broken
                // database, and must not stamp the 24h budget at the moment of the first failure.
                // Later retries are spaced a full interval from the previous retry.
                var retriedBefore = _lastLoadRetryTicks != 0L;
                var since = utcNow.Ticks - (retriedBefore ? _lastLoadRetryTicks : _lastLoadAttemptTicks);
                var delay = retriedBefore ? HistoryLoadRetryInterval : HistoryLoadFirstRetryDelay;

                // Wall clock on purpose (testable); a backwards NTP step makes the delta
                // negative — treat anything below zero as "delay elapsed" so a clock
                // correction cannot silently suppress retries.
                if (since >= 0 && since < delay.Ticks)
                    return;

                _lastLoadRetryTicks = utcNow.Ticks;

                // What enables the rerun is LoadHistoryUnderLock itself — it bypasses
                // Initialize()'s _isInitialized gate. _isInitialized deliberately stays set:
                // clearing it would let a reentrant TryAddValue (the SensorExpired ->
                // SetExpiredSnapshot path) re-enter Initialize() on a now-populated Storage,
                // re-opening the #1296 hazard the Monitor.IsEntered guard above only warns
                // about. _historyLoaded needs no assignment here: HistoryLoadFailed being true
                // already implies it is false, and LoadHistoryUnderLock sets it on completion.
                LoadHistoryUnderLock(isRetry: true);
            }
        }

        // Must be called with _lock held. Publishes the latch (on success OR failure) via its
        // finally block, so a caller below can never observe an unlatched sensor.
        //
        // isRetry is passed explicitly because it is not inferable from Storage: a live sensor
        // can have a null LastValue (timeout-only traffic, values rejected by validation, or a
        // retention purge / Clear(to) that emptied the cache mid-tick), so "Storage looks empty"
        // does not mean "no live ingestion". The two modes differ in what they may write:
        //
        // - Cold load (Initialize, isRetry=false): ingestion is still blocked — TryAddValue
        //   parks in Initialize() on this same lock — so this is the only mode allowed to
        //   rebuild Storage and run the policy fan-out.
        // - Retry (isRetry=true): _isInitialized stays set, so ingestion runs lock-free and
        //   ValuesStorage is not safe for concurrent writes; a DB row replayed here races live
        //   ingestion (torn _lastValue/_to, a duplicated newest point in the Plotly cache), and
        //   TryValidate would reach SensorExpired -> SetExpiredSnapshot -> TryAddValue
        //   (notifications and DB writes from the maintenance sweep) plus force IsExpired from
        //   a stale timeout marker onto a possibly-reporting sensor. The retry is therefore
        //   write-free except for the timeout marker (ShouldDestroy's empty-cache fallback keys
        //   on it; the marker write touches neither _lastValue/_to nor the cache) and Cut
        //   below (a bare _from assignment; From stays MinValue after a failed load otherwise).
        //   The retry's purpose — latching _historyLoaded so self-destroy decisions stop being
        //   deferred — needs nothing more. Limits: a retried sensor's cache, IsExpired and TTL
        //   clocks are NOT restored; see aicontext/features/server/overview.md (#1344).
        private void LoadHistoryUnderLock(bool isRetry)
        {
            // Every attempt stamps the clock, failed or not: the FIRST retry is measured from
            // the failure itself, so the sweep that observed the failure cannot fire a
            // back-to-back attempt against a possibly still-broken database.
            _lastLoadAttemptTicks = DateTime.UtcNow.Ticks;

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
                    else if (last.IsTimeout)
                    {
                        Storage.AddTimeoutMarker((T)last);
                    }

                    if (first != null)
                        Storage.Cut(first.Time);
                }

                // Before the log line: an NLog throw must not classify a successful load as failed.
                _historyLoaded = true;
                _logger.Info($"Sensor {Id} initialized {From}-{To}");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Sensor initialization error {Id}");
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
