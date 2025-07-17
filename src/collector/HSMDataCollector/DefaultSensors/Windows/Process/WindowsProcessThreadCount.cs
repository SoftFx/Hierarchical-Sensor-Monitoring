using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    public sealed class WindowsProcessThreadCount : WindowsSensorBase
    {
        protected override string CategoryName => "Process";

        protected override string CounterName => "Thread Count";

        protected override string InstanceName { get; } = ProcessInfo.CurrentProcessName;



        internal WindowsProcessThreadCount(BarSensorOptions options) : base(options) { }
    }
}
