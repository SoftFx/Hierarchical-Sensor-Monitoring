using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model
{
    public abstract class ValuesStorage
    {
        internal abstract bool HasData { get; }

        internal abstract BaseValue LastValue { get; }


        internal abstract void Clear();

        internal abstract List<BaseValue> GetValues(int count);

        internal abstract List<BaseValue> GetValues(DateTime from, DateTime to);
    }


    public abstract class ValuesStorage<T> : ValuesStorage where T : BaseValue
    {
        private readonly ConcurrentQueue<T> _cachedValues = new();


        protected virtual int CacheSize => 100;

        internal override bool HasData => !_cachedValues.IsEmpty;

        internal override BaseValue LastValue => _cachedValues.LastOrDefault();


        internal virtual T AddValueBase(T value)
        {
            if (value == null)
                return null;

            _cachedValues.Enqueue(value);

            if (_cachedValues.Count > CacheSize)
                _cachedValues.TryDequeue(out _);

            return value;
        }

        internal virtual T AddValue(T value) => AddValueBase(value);

        internal override void Clear() => _cachedValues.Clear();

        internal override List<BaseValue> GetValues(int count) =>
            _cachedValues.Take(count).Select(v => (BaseValue)v).ToList();

        internal override List<BaseValue> GetValues(DateTime from, DateTime to) =>
            _cachedValues.Where(v => v.ReceivingTime >= from && v.ReceivingTime <= to).Select(v => (BaseValue)v).ToList();
    }
}
