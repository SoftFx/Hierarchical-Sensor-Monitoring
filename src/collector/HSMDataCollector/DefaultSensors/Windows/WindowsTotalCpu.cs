namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsTotalCpu : WindowsSensorBase
    {
        protected override string CategoryName => "Processor";

        protected override string CounterName => "% Processor Time";

        protected override string InstanceName => "_Total";


        protected override string SensorName => "Total CPU";


        internal WindowsTotalCpu(string nodePath) : base(nodePath) { }
    }
}
