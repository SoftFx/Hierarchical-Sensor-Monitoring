using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Other
{
    internal sealed class CollectorAlive : MonitoringSensorBase<bool>
    {
        public CollectorAlive(MonitoringInstantSensorOptions options) : base(options) { }


        protected override bool GetValue() => true;
    }
}
