using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsProcessThreadCount : WindowsSensorBase
    {
        protected override string CategoryName => "Process";

        protected override string CounterName => "Thread Count";

        protected override string InstanceName { get; } = ProcessInfo.CurrentProcessName;


        protected override string SensorName => "Process thread count";


        internal WindowsProcessThreadCount(BarSensorOptions options) : base(options) { }
    }
}
