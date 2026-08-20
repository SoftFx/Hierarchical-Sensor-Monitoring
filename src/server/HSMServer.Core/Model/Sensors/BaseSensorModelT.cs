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

            // Monitor is reentrant, and _isInitialized is deliberately still false while the load
            // runs, so a same-thread re-entry would replay the whole history load: the TryValidate
            // calls below can reach SensorTimeout -> SensorExpired -> TryAddValue -> Initialize.
            // Returning here stops the replay but leaves that nested caller running against a
            // half-built Storage — #1296 on the initializing thread. Unreachable today only by an
            // ordering accident (HasData is false at both TryValidate sites), so log it: if a
            // policy change ever makes it reachable, this line is the only thing that will say so.
            if (Monitor.IsEntered(_lock))
            {
                _logger.Warn($"Reentrant Initialize on sensor {Id} during history load — the nested value path sees a partial Storage (#1296)");
                return;
            }

            lock (_lock)
            {
                if (_isInitialized)
                    return;

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

                        if (last.IsTimeout)
                        {
                            var valueBytes = _database.GetLatestValue(Id, last.Time.Ticks-1);
                            var value = valueBytes != null ? Convert(valueBytes) : null;

                            if (value is not null && !value.IsTimeout && Policies.TryValidate(value, out _))
                                Storage.AddValue((T)value);

                            IsExpired = true;
                            foreach (var ttl in Policies.TTLPolicies)
                                ttl.InitLastTtlTime(last.Time);
                        }

                        if (last.IsTimeout || Policies.TryValidate(last, out _))
                            Storage.AddValue((T)last);

                        if (first != null)
                            Storage.Cut(first.Time);
                    }

                    _logger.Info($"Sensor {Id} initialized {From}-{To}");
                    _historyLoaded = true;
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
                    // aicontext/features/server/overview.md, BaseSensorModel<T>.
                    _isInitialized = true;
                }
            }

        }
    }
}