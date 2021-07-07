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
        public DefaultValueSensor(string path, string productKey, IValuesQueue queue, SensorType type, string description = "")
            : base(path, productKey, queue)
        {
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
            throw new NotImplementedException();
        }

        protected override string GetStringData(SensorValueBase data)
        {
            throw new NotImplementedException();
        }

        public override FullSensorValue GetLastValueNew()
        {
            throw new NotImplementedException();
        }

        public void AddValue(T value, string comment = "")
        {
            throw new NotImplementedException();
        }

        public void AddValue(T value, SensorStatus status = SensorStatus.Unknown, string comment = "")
        {
            throw new NotImplementedException();
        }
    }
}
