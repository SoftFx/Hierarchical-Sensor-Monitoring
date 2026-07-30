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
        private readonly object _lock = new();

        protected BaseSensorModel(SensorEntity entity, IDatabaseCore database) : base(entity) 
        {
            _database = database;

            if (database == null)
                _isInitialized = true;
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
            // Monitor is reentrant, and _isInitialized is deliberately still false while the load
            // runs, so a same-thread re-entry would replay the whole history load: the TryValidate
            // calls below can reach SensorTimeout -> SensorExpired -> TryAddValue -> Initialize.
            // That path is currently unreachable only by an ordering accident (HasData is still
            // false at both TryValidate sites), which nothing in the code states or enforces.
            if (_isInitialized || Monitor.IsEntered(_lock))
                return;

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
                        first = Convert(firstBytes);

                        if (last.IsTimeout)
                        {
                            var valueBytes = _database.GetLatestValue(Id, last.Time.Ticks-1);
                            var value = Convert(valueBytes);

                            if (!value.IsTimeout && Policies.TryValidate(value, out _))
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
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Sensor initialization error {Id}");
                }
                finally
                {
                    // Published once the load has finished OR failed, so the three value-ingress
                    // gates (TryAddValue/TryUpdateLastValue/CheckTimeout) park on _lock instead of
                    // racing past on an empty Storage (#1296). Two limits this does NOT cover:
                    // a failed load latches too and publishes an empty Storage — deliberate, one
                    // loud error above beats a per-value retry storm against a broken database —
                    // and only those three gates wait; direct Storage readers (LastValue, HasData,
                    // Revalidate, Cut, ...) stay unguarded exactly as they were before #1296.
                    _isInitialized = true;
                }
            }

        }
    }
}