using HSMServer.Core.Extensions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

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


        internal abstract List<BaseValue> GetValues(DateTime from, DateTime to);

        internal abstract List<BaseValue> GetValues(int count);

        internal abstract bool TryChangeLastValue(BaseValue value, bool changeLast = false);

        internal abstract BaseValue GetEmptyValue();

        internal abstract void Clear(DateTime to);

        internal abstract void Clear();
    }


    public abstract class ValuesStorage<T> : ValuesStorage where T : BaseValue, new()
    {
        private readonly ConcurrentQueue<T> _cache = new();

        private T _lastValue, _lastTimeout;


        internal override T LastDbValue => _cache.LastOrDefault();

        internal override T LastTimeout => _lastTimeout;

        internal override T LastValue => _lastValue;

        internal override bool HasData => !_cache.IsEmpty;


        internal virtual void AddValue(T value) => AddValueBase(value);

        internal virtual void AddValueBase(T value)
        {
            if (value.IsTimeout)
            {
                if (_lastTimeout is null || _lastTimeout.ReceivingTime < value.ReceivingTime)
                    _lastTimeout = value;
            }
            else
            {
                _cache.Enqueue(value);

                if (_cache.Count > CacheSize)
                    _cache.TryDequeue(out _);

                if (_lastValue is null || value.Time >= _lastValue.Time)
                    _lastValue = value;
            }
        }

        internal override bool TryChangeLastValue(BaseValue value, bool changeLast = false)
        {
            if (!changeLast)
            {
                AddValue((T)value);
                return true;
            }
            
            if (_cache.TryDequeue(out _) || _cache.IsEmpty)
            {
                AddValue((T)value);
                return true;
            }

            return false;
        }

        internal override BaseValue GetEmptyValue() => new T();

        internal override List<BaseValue> GetValues(int count) =>
            _cache.Take(count).Select(v => (BaseValue)v).ToList();

        internal override List<BaseValue> GetValues(DateTime from, DateTime to) =>
            _cache.Where(v => v.InRange(from, to)).Select(u => (BaseValue)u).ToList();

        internal override void Clear(DateTime to)
        {
            while (_cache.FirstOrDefault()?.ReceivingTime <= to)
                _cache.TryDequeue(out _);

            if (_cache.IsEmpty)
                _lastValue = null;
        }

        internal override void Clear()
        {
            _cache.Clear();

            _lastValue = null;
        }
    }
}