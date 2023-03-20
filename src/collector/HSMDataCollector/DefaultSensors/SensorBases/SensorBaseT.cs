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
        

        protected void SendValue<T>()
        {
            base.SendValue(BuildSensorValue());
        }
        
        protected abstract T GetValue();


        protected static SensorValueBase BuildSensorValue(string comment = default, SensorStatus status = SensorStatus.Ok )
        {
            return BuildSensorValue();
        }
        
        protected SensorValueBase BuildSensorValue()
        {
            try
            {
                var value = SensorValuesFactory.BuildValue(GetValue());

                return value.Complete(SensorPath, GetComment(), GetStatus());
            }
            catch (Exception ex)
            {
                var value = SensorValuesFactory.BuildValue(default(T));

                return value.Complete(SensorPath, ex.Message, SensorStatus.Error);
            }
        }
    }
}