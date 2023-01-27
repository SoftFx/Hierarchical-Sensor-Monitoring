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
        protected bool CanSendValue { get; set; } = true;


        protected MonitoringSensorBase(SensorOptions options) : base(options) { }


        internal override void Stop()
        {
            base.Stop();

            SendValue();
        }

        protected override void OnTimerTick(object _ = null) => SendValue();

        protected abstract T GetValue();

        private void SendValue()
        {
            var value = BuildValue();

            if (CanSendValue)
                SendCollectedValue(value);
        }

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
