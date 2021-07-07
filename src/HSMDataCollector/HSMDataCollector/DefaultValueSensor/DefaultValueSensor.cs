using HSMDataCollector.Base;
using HSMDataCollector.Core;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using System;

namespace HSMDataCollector.DefaultValueSensor
{
    internal class DefaultValueSensor<T> : SensorBase, ILastValueSensor<T>
    {
        private readonly string _description;
        private readonly SensorType _type;
        protected readonly object _syncRoot;
        protected T _currentValue;
        protected string _currentComment;
        protected SensorStatus _currentStatus;
        public DefaultValueSensor(string path, string productKey, IValuesQueue queue, SensorType type, T defaultValue, string description = "")
            : base(path, productKey, queue)
        {
            lock (_syncRoot)
            {
                _currentValue = defaultValue;
            }
            _description = description;
            _type = type;
        }

        public override bool HasLastValue => true;
        public override CommonSensorValue GetLastValue()
        {
            throw new NotImplementedException();
        }

        public override void Dispose()
        {
            
        }

        protected override string GetStringData(SensorValueBase data)
        {
            throw new NotImplementedException();
        }

        public override FullSensorValue GetLastValueNew()
        {
            FullSensorValue value = new FullSensorValue();
            value.Type = _type;
            value.Key = ProductKey;
            value.Path = Path;
            value.Time = DateTime.Now;
            value.Description = _description;
            lock (_syncRoot)
            {
                value.Data = _currentValue.ToString();
                value.Comment = _currentComment;
                value.Status = _currentStatus;
            }

            return value;
        }

        public void AddValue(T value, string comment = "")
        {
            lock (_syncRoot)
            {
                _currentValue = value;
                _currentComment = comment;
            }
        }

        public void AddValue(T value, SensorStatus status = SensorStatus.Unknown, string comment = "")
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
