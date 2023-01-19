using HSMDataCollector.Extensions;
using HSMDataCollector.SensorsFactory;
using HSMSensorDataObjects;
using System;

namespace HSMDataCollector.DefaultSensors
{
    public abstract class MonitoringSensorBase<T> : MonitoringSensorBase
    {
        protected MonitoringSensorBase(string nodePath) : base(nodePath) { }


        protected abstract T GetValue();

        protected override void OnTimerTick(object _ = null)
        {
            try
            {
                var value = SensorValuesFactory.BuildValue(GetValue());

                SendCollectedValue(value.Complete(SensorPath));
            }
            catch (Exception ex)
            {
                var value = SensorValuesFactory.BuildValue(default(T));

                SendCollectedValue(value.Complete(SensorPath, SensorStatus.Error, ex.Message));
            }
        }
    }
}
