using HSMDataCollector.Base;
using HSMDataCollector.Core;
using HSMDataCollector.PublicInterface;
using HSMDataCollector.SensorsFactory;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;

namespace HSMDataCollector.DefaultValueSensor
{
    internal class DefaultValueSensor<T> : SensorBase, ILastValueSensor<T>
    {
        private readonly SensorType _type;
        protected readonly object _syncRoot = new object();
        protected T _currentValue;
        protected string _currentComment;
        protected SensorStatus _currentStatus;
        public DefaultValueSensor(string path, string productKey, IValuesQueue queue, SensorType type, T defaultValue, string description = "")
            : base(path, productKey, queue, description)
        {
            lock (_syncRoot)
            {
                _currentValue = defaultValue;
            }
            _type = type;
        }

        public override bool HasLastValue => true;

        public override void Dispose()
        {

        }

        public override SensorValueBase GetLastValue()
        {
            var value = SensorValuesFactory.BuildValue(_currentValue);

            value.Key = ProductKey;
            value.Path = Path;
            value.Time = DateTime.Now;
            lock (_syncRoot)
            {
                value.Comment = _currentComment;
                value.Status = _currentStatus;
            }

            return value;
        }

        public void AddValue(T value)
        {
            lock (_syncRoot)
            {
                _currentValue = value;
            }
        }

        public void AddValue(T value, string comment = "")
        {
            lock (_syncRoot)
            {
                _currentValue = value;
                _currentComment = comment;
            }
        }

        public void AddValue(T value, SensorStatus status = SensorStatus.Ok, string comment = "")
        {
            lock (_syncRoot)
            {
                _currentValue = value;
                _currentStatus = status;
                _currentComment = comment;
            }
        }
    }
}
