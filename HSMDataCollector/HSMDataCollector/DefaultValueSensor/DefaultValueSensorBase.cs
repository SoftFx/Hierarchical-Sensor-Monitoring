using HSMDataCollector.Base;
using HSMDataCollector.Core;
using HSMSensorDataObjects;

namespace HSMDataCollector.DefaultValueSensor
{
    internal abstract class DefaultValueSensorBase<T> : ISensor
    {
        protected readonly object _syncRoot;
        protected T _currentValue;
        protected DefaultValueSensorBase(string path, string productKey, IValuesQueue queue, T defaultValue)
        {
            _syncRoot = new object();
            lock (_syncRoot)
            {
                _currentValue = defaultValue;
            }
        }

        public bool HasLastValue => true;
        public abstract CommonSensorValue GetLastValue();
        public void Dispose()
        {
            
        }
    }
}
