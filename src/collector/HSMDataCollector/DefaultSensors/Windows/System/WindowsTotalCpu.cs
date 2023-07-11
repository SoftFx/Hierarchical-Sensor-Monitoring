using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsTotalCpu : WindowsSensorBase
    {
        protected override string SensorName => "Total CPU";

        protected override string CategoryName => "Processor";

        protected override string CounterName => "% Processor Time";

        protected override string InstanceName => "_Total";



        internal WindowsTotalCpu(BarSensorOptions options) : base(options) { }
    }
}
