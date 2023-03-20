using System;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMDataCollector.SensorsFactory;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;

namespace HSMDataCollector.DefaultSensors.SensorBases
{
    public abstract class SensorBase<T> : SensorBase 
    {
        protected SensorBase(SensorOptions options) : base(options) { }
        
        protected abstract T GetValue();


        protected void SendValue(T value, string comment = "", SensorStatus status = SensorStatus.Ok)
        {
            SendValue(GetSensorValue(value).Complete(SensorPath, comment, status));
        }

        protected static SensorValueBase GetSensorValue(T value) => SensorValuesFactory.BuildValue(value);
    }
}