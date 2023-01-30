using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Common
{
    internal sealed class CollectorAlive : MonitoringSensorBase<bool>
    {
        protected override string SensorName => "Service alive";


        public CollectorAlive(SensorOptions options) : base(options) { }


        protected override bool GetValue() => true;
    }
}
