using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Diagnostic
{
    internal sealed class PackageDataCountSensor : BaseQueueInfoIntSensor
    {
        public PackageDataCountSensor(BarSensorOptions options) : base(options) { }
    }
}