using System;
using HSMDataCollector.Base;
using HSMDataCollector.Core;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;

namespace HSMDataCollector.DefaultValueSensor
{
    [Obsolete("07.07.2021. Use DefaultValueSensor")]
    internal abstract class DefaultValueSensorBase<T> : ISensor
    {
        protected readonly object _syncRoot;
        protected T _currentValue;
        protected string _currentComment;
        protected SensorStatus _currentStatus;
        protected string Path;
        protected string ProductKey;
        protected DefaultValueSensorBase(string path, string productKey, IValuesQueue queue, T defaultValue)
        {
            Path = path;
            ProductKey = productKey;
            _syncRoot = new object();
            lock (_syncRoot)
            {
                _currentValue = defaultValue;
            }
        }

        public bool HasLastValue => true;
        public abstract CommonSensorValue GetLastValue();
        public SensorValueBase GetLastValueNew()
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            
        }
    }
}
