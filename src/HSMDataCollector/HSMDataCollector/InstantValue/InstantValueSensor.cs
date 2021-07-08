using HSMDataCollector.Base;
using HSMDataCollector.Core;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using System;

namespace HSMDataCollector.InstantValue
{
    class InstantValueSensor<T> : SensorBase, IInstantValueSensor<T>
    {
        private readonly string _description;
        private readonly SensorType _type;
        public InstantValueSensor(string path, string productKey, IValuesQueue queue, SensorType type, string description = "")
            : base(path, productKey, queue)
        {
            _description = description;
            _type = type;
        }

        public override bool HasLastValue => false;
        public override CommonSensorValue GetLastValue()
        {
            throw new NotImplementedException();
        }

        public override SimpleSensorValue GetLastValueNew()
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

        public void AddValue(T value, string comment = "")
        {
            SimpleSensorValue valueObject = new SimpleSensorValue();
            valueObject.Comment = comment;
            valueObject.Data = value.ToString();
            valueObject.Description = _description;
            valueObject.Path = Path;
            valueObject.Key = ProductKey;
            valueObject.Time = DateTime.Now;
            valueObject.Type = _type;
            EnqueueValue(valueObject);
        }
        
        public void AddValue(T value, SensorStatus status = SensorStatus.Unknown, string comment = "")
        {
            SimpleSensorValue valueObject = new SimpleSensorValue();
            valueObject.Comment = comment;
            valueObject.Data = value.ToString();
            valueObject.Description = _description;
            valueObject.Path = Path;
            valueObject.Key = ProductKey;
            valueObject.Time = DateTime.Now;
            valueObject.Type = _type;
            valueObject.Status = status;
            EnqueueValue(valueObject);
        }
    }
}
