using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using HSMCommon.Extensions;
using HSMCommon.Model;
using HSMServer.Core.Extensions;


namespace HSMServer.Core.Model
{
    public abstract class ValuesStorage
    {
        protected virtual int CacheSize => 100;


        internal SensorResult? Result => LastValue != null ? new SensorResult(LastValue) : null;


        internal abstract BaseValue LastTimeout { get; }

        internal abstract BaseValue LastDbValue { get; }

        internal abstract BaseValue LastValue { get; }

        internal abstract bool HasData { get; }

        internal abstract DateTime From { get; set; }

        internal abstract DateTime To { get; }

        internal abstract List<BaseValue> GetValues(DateTime from, DateTime to);

        internal abstract List<BaseValue> GetValues(int count);

        internal abstract bool TryChangeLastValue(BaseValue value);

        internal abstract BaseValue GetEmptyValue();

        internal abstract void Clear(DateTime to);

        internal abstract void Clear();
    }


    public abstract class ValuesStorage<T> : ValuesStorage where T : BaseValue, new()
    {
        private readonly ConcurrentQueue<T> _cache = new();
        private readonly TimeSpan _singletonTimePrecision = TimeSpan.FromSeconds(1);

        private T _lastValue, _lastTimeout;

        private DateTime? _from, _to;
        private readonly Func<BaseValue> _getFirstValue, _getLastValue;

        private readonly object _lock = new();

        private bool IsLastEmptyOrTimeout => LastValue is null || LastTimeout?.ReceivingTime > LastValue.ReceivingTime;

        public ValuesStorage(Func<BaseValue> getFirstValue, Func<BaseValue> getLastValue)
        {
            _getFirstValue = getFirstValue ?? throw new ArgumentNullException(nameof(getFirstValue));
            _getLastValue = getLastValue ?? throw new ArgumentNullException(nameof(getLastValue));
        }

        internal override T LastDbValue => _cache.LastOrDefault();

        internal override T LastTimeout => _lastTimeout;

        internal override T LastValue => _lastValue;

        internal override bool HasData => !_cache.IsEmpty;

        internal override DateTime From
        {
            get
            {
                if (!_from.HasValue)
                    InitFirstValue();

                return _from.Value;
            }
            set
            {
                _from = value;
            }
        }

        internal override DateTime To
        {
            get
            {
                if (!_to.HasValue)
                    InitLastValue();

                return _to.Value;
            }
        }

        internal virtual T CalculateStatistics(T value) => value;

        internal virtual T RecalculateStatistics(T value) => value;


        internal virtual void AddValue(T value) => AddValueBase(value);

        internal virtual void AddValueBase(T value)
        {
            if (value.IsTimeout && (_lastTimeout is null || _lastTimeout.ReceivingTime < value.ReceivingTime))
                _lastTimeout = value;

            _cache.Enqueue(value);

            if (_cache.Count > CacheSize)
                _cache.TryDequeue(out _);

            if (_lastValue is null || value.Time >= _lastValue.Time)
            {
                _lastValue = value;
                _to = value.Time;
            }
        }

        internal override bool TryChangeLastValue(BaseValue value)
        {
            if (_cache.TryDequeue(out _) || _cache.IsEmpty)
            {
                AddValue((T)value);
                return true;
            }

            return false;
        }

        internal override BaseValue GetEmptyValue() => new T();

        internal bool TryAggregateValue(T value)
        {
            if (IsLastEmptyOrTimeout || !LastValue.TryAggregateValue(value))
            {
                AddValue(value);
                return false;
            }

            return true;
        }

        internal bool IsNewSingletonValue(BaseValue value) => IsLastEmptyOrTimeout || LastValue.Time.Floor(_singletonTimePrecision) < value.Time.Floor(_singletonTimePrecision);


        internal override List<BaseValue> GetValues(int count) =>
            _cache.Take(count).Select(v => (BaseValue)v).ToList();

        internal override List<BaseValue> GetValues(DateTime from, DateTime to) =>
            _cache.Where(v => v.InRange(from, to)).Select(u => (BaseValue)u).ToList();

        internal override void Clear(DateTime to)
        {
            while (_cache.FirstOrDefault()?.LastUpdateTime <= to)
                _cache.TryDequeue(out _);

            if (_cache.IsEmpty)
                _lastValue = null;
        }

        internal override void Clear()
        {
            _cache.Clear();

            _lastValue = null;
        }

        private void InitFirstValue()
        {
            lock (_lock)
            {
                if (!_from.HasValue)
                {
                    var item = _getFirstValue.Invoke();
                    if (item != null)
                    {
                        _from = item.Time;
                    }
                    else
                    {
                        _from = DateTime.MinValue;
                        _to   = DateTime.MaxValue;
                    }
                }
            }
        }

        private void InitLastValue()
        {
            lock (_lock)
            {
                if (!_to.HasValue)
                {
                    var item = _getLastValue.Invoke();
                    if (item != null)
                    {
                        AddValue((T)item);
                    }
                    else
                    {
                        _from = DateTime.MinValue;
                        _to   = DateTime.MaxValue;
                    }
                }
            }
        }
    }
}