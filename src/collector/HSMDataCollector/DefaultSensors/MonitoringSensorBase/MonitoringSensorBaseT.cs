using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMDataCollector.SensorsFactory;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;

namespace HSMDataCollector.DefaultSensors
{
    public abstract class MonitoringSensorBase<T> : MonitoringSensorBase
    {
        protected bool NeedSendValue { get; set; } = true;


        protected MonitoringSensorBase(SensorOptions options) : base(options) { }


        internal override void Stop()
        {
            base.Stop();

            OnTimerTick();
        }

        protected sealed override void OnTimerTick(object _ = null)
        {
            var value = BuildValue();

            if (NeedSendValue)
                SendCollectedValue(value);
        }

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
