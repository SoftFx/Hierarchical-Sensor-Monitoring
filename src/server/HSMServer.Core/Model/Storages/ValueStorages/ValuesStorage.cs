using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model
{
    public abstract class ValuesStorage
    {
        protected abstract int CacheSize { get; }

        internal abstract string LatestValueInfo { get; }

        internal abstract DateTime LastUpdateTime { get; }

        internal abstract bool HasData { get; }


        internal abstract void Clear();
    }


    public abstract class ValuesStorage<T> : ValuesStorage where T : BaseValue
    {
        private readonly ConcurrentQueue<T> _cachedValues = new();


        protected override int CacheSize => 100;

        internal override string LatestValueInfo => _cachedValues.LastOrDefault()?.ShortInfo;

        internal override DateTime LastUpdateTime => _cachedValues.LastOrDefault()?.ReceivingTime ?? DateTime.MinValue;

        internal override bool HasData => !_cachedValues.IsEmpty;


        internal virtual T AddValue(T value)
        {
            _cachedValues.Enqueue(value);

            if (_cachedValues.Count > CacheSize)
                _cachedValues.TryDequeue(out _);

            return value;
        }

        internal override void Clear() => _cachedValues.Clear();
    }
}
