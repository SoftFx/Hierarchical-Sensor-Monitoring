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

        // Bounded retry of failed loads (#1344): the maintenance sweep re-arms the latch at most
        // once per this interval, so a broken database sees at most one retry attempt per sensor
        // per day, never a per-value storm.
        private static readonly TimeSpan HistoryLoadRetryInterval = TimeSpan.FromHours(24);

        // Ticks of the last RetryFailedHistoryLoad re-arm (0 = never); guarded by _lock.
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

                LoadHistoryUnderLock();
            }

        }

        // Re-arm the failed-load latch and rerun the load, at most once per interval (#1344).
        // Called only from the maintenance sweep (hourly), so a broken database sees at most one
        // load attempt per sensor per interval — not the per-value retry storm the latch prevents
        // (#1296). The interval gate is checked and stamped inside the lock so concurrent sweeps
        // cannot double-fire it.
        internal override void RetryFailedHistoryLoad(DateTime utcNow)
        {
            if (!HistoryLoadFailed)
                return;

            lock (_lock)
            {
                if (!HistoryLoadFailed)
                    return;

                // Wall clock on purpose (testable); a backwards NTP step makes the delta
                // negative — treat anything below zero as "interval elapsed" so a clock
                // correction cannot silently suppress retries.
                var sinceLastRetry = utcNow.Ticks - _lastLoadRetryTicks;
                if (sinceLastRetry >= 0 && sinceLastRetry < HistoryLoadRetryInterval.Ticks)
                    return;

                _lastLoadRetryTicks = utcNow.Ticks;

                // Only _historyLoaded flips: _isInitialized must stay true. Clearing it would make
                // a reentrant TryAddValue (SensorExpired -> SetExpiredSnapshot path) re-enter
                // Initialize() on a Storage that is now populated, re-opening the #1296 hazard the
                // Monitor.IsEntered guard above only warns about. With _isInitialized left set the
                // guard contract ("Storage is safe to read lock-free") still holds during a reload:
                // Storage is non-empty and only gains values.
                _historyLoaded = false;

                LoadHistoryUnderLock();
            }
        }

        // Must be called with _lock held. Publishes the latch (on success OR failure) via its
        // finally block, so a caller below can never observe an unlatched sensor.
        private void LoadHistoryUnderLock()
        {
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

                    // A retry (RetryFailedHistoryLoad) can run against a *hot* sensor: after the
                    // failed load the sensor kept reporting, so Storage holds live values. On a
                    // hot Storage the sweep must not write anything except Cut below: the value
                    // paths run lock-free once _isInitialized is set, and ValuesStorage is not
                    // thread-safe for concurrent writes, so a DB row added here races live
                    // ingestion (torn _lastValue/_to, a duplicated newest point in the Plotly
                    // cache). Policy fan-out must not run either: TryValidate reaches
                    // SensorExpired -> SetExpiredSnapshot -> TryAddValue (notifications and DB
                    // writes from the maintenance sweep), and a stale timeout marker as the
                    // newest DB row must not force IsExpired onto a currently-reporting sensor.
                    // The retry's actual purpose — latching _historyLoaded so self-destroy
                    // decisions stop being deferred — is satisfied without touching live values.
                    // On a cold load (Initialize) Storage is empty, so the original first-load
                    // body below runs unchanged.
                    if (Storage.LastValue is null)
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
                // aicontext/features/server/overview.md, BaseSensorModel<T>. Bounded re-arm:
                // RetryFailedHistoryLoad (#1344).
                _isInitialized = true;
            }
        }
    }
}