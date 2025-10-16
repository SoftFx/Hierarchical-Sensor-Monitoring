using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    public sealed class WindowsTotalCpu : WindowsSensorBase
    {
        protected override string CategoryName => "Processor";

        protected override string CounterName => "% Processor Time";

        protected override string InstanceName => TotalInstance;



        internal WindowsTotalCpu(BarSensorOptions options) : base(options) { }
    }
}
