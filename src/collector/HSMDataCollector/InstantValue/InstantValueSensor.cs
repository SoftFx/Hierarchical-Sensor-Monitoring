using HSMDataCollector.Base;
using HSMDataCollector.Core;
using HSMDataCollector.PublicInterface;
using HSMDataCollector.SensorsFactory;
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
            : base(path, productKey, queue, description)
        {
            _description = description;
            _type = type;
        }

        public override bool HasLastValue => false;
        public override SensorValueBase GetLastValue()
        {
            throw new NotImplementedException();
        }

        public override void Dispose()
        {

        }

        public void AddValue(T value)
        {
            var valueObject = SensorValuesFactory.BuildValue(value);

            valueObject.Comment = _description;
            valueObject.Path = Path;
            valueObject.Key = ProductKey;
            valueObject.Time = DateTime.Now;
            valueObject.Status = SensorStatus.Ok;

            EnqueueValue(valueObject);
        }

        public void AddValue(T value, string comment = "")
        {
            var valueObject = SensorValuesFactory.BuildValue(value);

            valueObject.Comment = comment;
            valueObject.Path = Path;
            valueObject.Key = ProductKey;
            valueObject.Time = DateTime.Now;
            valueObject.Status = SensorStatus.Ok;

            EnqueueValue(valueObject);
        }

        public void AddValue(T value, SensorStatus status = SensorStatus.Ok, string comment = "")
        {
            var valueObject = SensorValuesFactory.BuildValue(value);

            valueObject.Comment = comment;
            valueObject.Path = Path;
            valueObject.Key = ProductKey;
            valueObject.Time = DateTime.Now;
            valueObject.Status = status;

            EnqueueValue(valueObject);
        }
    }
}
