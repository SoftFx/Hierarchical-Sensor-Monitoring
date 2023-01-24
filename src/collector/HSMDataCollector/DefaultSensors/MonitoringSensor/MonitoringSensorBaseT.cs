using HSMDataCollector.Extensions;
using HSMDataCollector.SensorsFactory;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;

namespace HSMDataCollector.DefaultSensors
{
    public abstract class MonitoringSensorBase<T> : MonitoringSensorBase
    {
        protected MonitoringSensorBase(string nodePath) : base(nodePath) { }


        internal override SensorValueBase GetLastValue() => BuildValue();

        protected abstract T GetValue();

        protected override void OnTimerTick(object _ = null) => SendCollectedValue(BuildValue());

        private SensorValueBase BuildValue()
        {
            try
            {
                var value = SensorValuesFactory.BuildValue(GetValue());

                return value.Complete(SensorPath);
            }
            catch (Exception ex)
            {
                var value = SensorValuesFactory.BuildValue(default(T));

                return value.Complete(SensorPath, SensorStatus.Error, ex.Message);
            }
        }
    }
}
