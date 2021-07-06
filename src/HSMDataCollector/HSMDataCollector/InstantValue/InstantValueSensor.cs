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
        private string _description;
        public InstantValueSensor(string path, string productKey, IValuesQueue queue, string description = "")
            : base(path, productKey, queue)
        {
            _description = description;
        }

        public override bool HasLastValue => false;
        public override CommonSensorValue GetLastValue()
        {
            throw new NotImplementedException();
        }

        public override FullSensorValue GetLastValueNew()
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
            FullSensorValue valueObject = new FullSensorValue();
            valueObject.Comment = comment;
            valueObject.Data = value.ToString();
            valueObject.Description = _description;
            valueObject.Path = Path;
            valueObject.Key = ProductKey;
            valueObject.Time = DateTime.Now;
            //Send values
            //SendData
        }

        public void AddValue(T value, SensorStatus status = SensorStatus.Unknown)
        {
            throw new NotImplementedException();
        }

        public void AddValue(T value, SensorStatus status = SensorStatus.Unknown, string comment = "")
        {
            throw new NotImplementedException();
        }
    }
}
