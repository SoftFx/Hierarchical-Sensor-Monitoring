using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Other
{
    internal sealed class CollectorHeartbeat : MonitoringSensorBase<bool>
    {
        protected override string SensorName => "Service heartbeat";


        public CollectorHeartbeat(MonitoringSensorOptions options) : base(options) { }


        protected override bool GetValue() => true;
    }
}
