using HSMDataCollector.Base;
using HSMDataCollector.Core;
using HSMDataCollector.PublicInterface;
using HSMDataCollector.SensorsFactory;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;

namespace HSMDataCollector.DefaultValueSensor
{
    internal sealed class DefaultValueSensor<T> : SensorBase, ILastValueSensor<T>
    {
        private readonly object _syncRoot = new object();
        private T _currentValue;
        private string _currentComment;
        private SensorStatus _currentStatus;

        public override bool HasLastValue => true;


        public DefaultValueSensor(string path, IValuesQueue queue, T defaultValue, string description = "")
            : base(path, queue, description)
        {
            lock (_syncRoot)
            {
                _currentValue = defaultValue;
            }
        }


        public override void Dispose() { }

        public override SensorValueBase GetLastValue()
        {
            var value = SensorValuesFactory.BuildValue(_currentValue);

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
