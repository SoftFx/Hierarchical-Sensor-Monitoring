using HSMDataCollector.Base;
using HSMDataCollector.Core;
using HSMDataCollector.PublicInterface;
using HSMDataCollector.SensorsFactory;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;

namespace HSMDataCollector.InstantValue
{
    internal sealed class InstantValueSensor<T> : SensorBase, IInstantValueSensor<T>
    {
        private readonly string _description;

        public override bool HasLastValue => false;


        public InstantValueSensor(string path, string productKey, IValuesQueue queue, SensorType type, string description = "")
            : base(path, productKey, queue, description)
        {
            _description = description;
        }


        public override SensorValueBase GetLastValue()
        {
            throw new NotImplementedException();
        }

        public override void Dispose() { }

        public void AddValue(T value)
        {
            var valueObject = SensorValuesFactory.BuildValue(value);

            valueObject.Comment = _description;
            valueObject.Path = Path;
            valueObject.Time = DateTime.Now;
            valueObject.Status = SensorStatus.Ok;

            EnqueueValue(valueObject);
        }

        public void AddValue(T value, string comment = "")
        {
            var valueObject = SensorValuesFactory.BuildValue(value);

            valueObject.Comment = comment;
            valueObject.Path = Path;
            valueObject.Time = DateTime.Now;
            valueObject.Status = SensorStatus.Ok;

            EnqueueValue(valueObject);
        }

        public void AddValue(T value, SensorStatus status = SensorStatus.Ok, string comment = "")
        {
            var valueObject = SensorValuesFactory.BuildValue(value);

            valueObject.Comment = comment;
            valueObject.Path = Path;
            valueObject.Time = DateTime.Now;
            valueObject.Status = status;

            EnqueueValue(valueObject);
        }
    }
}
