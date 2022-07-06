using HSMServer.Core.DataLayer;
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


        internal abstract bool AddValue(BaseValue value);

        internal abstract void AddValue(byte[] valueBytes);

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


        internal override bool AddValue(BaseValue value)
        {
            if (value != null && value is T valueT)
                _cachedValues.Add(valueT);

            return true;
        }

        internal override void AddValue(byte[] valueBytes)
        {
            var value = valueBytes.ConvertToSensorValue<T>();

            if (value != null)
                _cachedValues.Add((T)value);
        }

        internal override void Clear() => _cachedValues.Clear();
    }
}
