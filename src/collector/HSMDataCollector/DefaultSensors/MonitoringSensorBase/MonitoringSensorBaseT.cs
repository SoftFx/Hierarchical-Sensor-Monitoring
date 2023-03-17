using HSMDataCollector.Options;
using System.Threading.Tasks;
using HSMDataCollector.DefaultSensors.SensorBases;

namespace HSMDataCollector.DefaultSensors
{
    public abstract class MonitoringSensorBase<T> : SensorBase<T>
    {
        protected bool NeedSendValue { get; set; } = true;


        protected MonitoringSensorBase(MonitoringSensorOptions options) : base(options) { }


        internal override Task Stop()
        {
            OnTimerTick();
            
            return base.Stop();;
        }

        protected void OnTimerTick(object _ = null)
        {
            if (NeedSendValue)
                SendValue(BuildSensorValue());
        }
    }
}
