using HSMDataCollector.SensorsFactory;

namespace HSMDataCollector.DefaultSensors
{
    public abstract class MonitoringSensorBase<T> : MonitoringSensorBase
    {
        protected MonitoringSensorBase(string nodePath) : base(nodePath) { }


        protected abstract T GetValue();

        protected sealed override void OnTimerTick(object _)
        {
            try
            {
                var value = SensorValuesFactory.BuildValue(GetValue());

                SendCollectedValue(value);
            }
            catch
            {

            }
        }
    }
}
