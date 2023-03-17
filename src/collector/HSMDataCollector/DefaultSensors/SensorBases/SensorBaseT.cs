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


        protected sealed override void SendValue() => SendCollectedValue(BuildValue());
        
        
        protected abstract T GetValue();

        private SensorValueBase BuildValue()
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