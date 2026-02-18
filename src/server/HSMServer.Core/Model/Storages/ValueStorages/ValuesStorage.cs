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

        internal abstract DateTime From { get; }

        internal abstract DateTime To { get; }

        internal abstract List<BaseValue> GetValues(DateTime from, DateTime to);

        internal abstract List<BaseValue> GetValues(int count);

        internal abstract bool TryChangeLastValue(BaseValue value);

        internal abstract void Clear(DateTime to);

        internal abstract void Clear();

        internal abstract void Cut(DateTime time);

    }


    public abstract class ValuesStorage<T> : ValuesStorage where T : BaseValue, new()
    {
        private readonly ConcurrentQueue<T> _cache = new();
        private readonly TimeSpan _singletonTimePrecision = TimeSpan.FromSeconds(1);

        private T _lastValue, _lastTimeout;

        DateTime _from = DateTime.MinValue;
        DateTime _to   = DateTime.MaxValue;

        private bool IsLastEmptyOrTimeout => LastValue is null || LastTimeout?.ReceivingTime > LastValue.ReceivingTime;

        internal override T LastDbValue => _cache.LastOrDefault();

        internal override T LastTimeout => _lastTimeout;

        internal override T LastValue => _lastValue;

        internal override bool HasData => !_cache.IsEmpty;

        internal override DateTime From => _from;
        internal override DateTime To => _to;

        internal virtual T CalculateStatistics(T value) => value;

        internal virtual T RecalculateStatistics(T value) => value;


        internal virtual void AddValue(T value) => AddValueBase(value);

        internal virtual void AddValueBase(T value)
        {
            if (value.IsTimeout && (_lastTimeout is null || _lastTimeout.ReceivingTime < value.ReceivingTime))
            {
                _lastTimeout = value;
            }
            else if (_lastValue is null || value.Time >= _lastValue.Time)
            {
                _lastValue = value;
                _to = value.Time;

                _cache.Enqueue(value);

                if (_cache.Count > CacheSize)
                    _cache.TryDequeue(out _);
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


        internal override void Cut(DateTime time)
        {
            _from = time;
        }

        internal override void Clear()
        {
            _cache.Clear();

            _lastValue = null;
        }
    }
}