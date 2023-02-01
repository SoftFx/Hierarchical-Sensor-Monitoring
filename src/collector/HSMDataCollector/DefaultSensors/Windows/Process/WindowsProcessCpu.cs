using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsProcessCpu : WindowsSensorBase
    {
        protected override string SensorName => "Process CPU";

        protected override string CategoryName => "Process";

        protected override string CounterName => "% Processor Time";

        protected override string InstanceName { get; } = ProcessInfo.CurrentProcessName;



        internal WindowsProcessCpu(BarSensorOptions options) : base(options) { }
    }
}
