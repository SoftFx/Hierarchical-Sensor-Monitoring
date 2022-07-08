using System;
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
        private readonly List<T> _cachedValues;


        protected override int CacheSize => 100;

        internal override string LatestValueInfo => _cachedValues.LastOrDefault()?.ShortInfo;

        internal override DateTime LastUpdateTime => _cachedValues.LastOrDefault()?.ReceivingTime ?? DateTime.MinValue;

        internal override bool HasData => _cachedValues.Count > 0;


        internal ValuesStorage()
        {
            _cachedValues = new(CacheSize);
        }


        internal virtual void AddValue(T value)
        {
            _cachedValues.Add(value);
        }

        internal override void Clear() => _cachedValues.Clear();
    }
}
