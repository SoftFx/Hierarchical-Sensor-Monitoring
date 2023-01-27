using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsProcessCpu : WindowsSensorBase
    {
        protected override string CategoryName => "Process";

        protected override string CounterName => "% Processor Time";

        protected override string InstanceName { get; } = ProcessInfo.CurrentProcessName;


        protected override string SensorName => "Process CPU";


        internal WindowsProcessCpu(BarSensorOptions options) : base(options) { }
    }
}
