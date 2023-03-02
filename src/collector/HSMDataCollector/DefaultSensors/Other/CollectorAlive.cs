using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Other
{
    internal sealed class CollectorAlive : MonitoringSensorBase<bool>
    {
        protected override string SensorName => "Service alive";


        public CollectorAlive(SensorOptions options) : base(options) { }


        protected override bool GetValue() => true;
    }
}
