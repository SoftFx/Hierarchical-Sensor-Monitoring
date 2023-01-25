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

        protected override void OnTimerTick(object _ = null) => SendCollectedValue(BuildValue());

        protected virtual string GetComment() => null;

        protected abstract T GetValue();

        private SensorValueBase BuildValue()
        {
            try
            {
                var value = SensorValuesFactory.BuildValue(GetValue());

                return value.Complete(SensorPath, GetComment());
            }
            catch (Exception ex)
            {
                var value = SensorValuesFactory.BuildValue(default(T));

                return value.Complete(SensorPath, ex.Message, SensorStatus.Error);
            }
        }
    }
}
