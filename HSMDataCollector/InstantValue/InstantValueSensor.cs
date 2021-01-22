using System;
using System.Collections.Generic;
using System.Text;

namespace HSMDataCollector.InstantValue
{
    class InstantValueSensor<T> : SensorBase
    {
        private T _lastValue;
        protected InstantValueSensor(string name, string path, string productKey) : base(name, path, productKey)
        {
        }

        public event EventHandler<T> InstantValueCollected;
        public void AddValue(T value)
        {
            OnInstantValueCollected(value);
            _lastValue = value;
        }

        private void OnInstantValueCollected(T value)
        {
            InstantValueCollected?.Invoke(this, value);
        }
    }
}
