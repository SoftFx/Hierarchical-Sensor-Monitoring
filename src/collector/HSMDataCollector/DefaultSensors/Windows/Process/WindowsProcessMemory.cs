using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsProcessMemory : WindowsSensorBase
    {
        protected override string CategoryName => "Process";

        protected override string CounterName => "Working set";

        protected override string InstanceName { get; } = ProcessInfo.CurrentProcessName;


        protected override string SensorName => "Process memory MB";


        internal WindowsProcessMemory(BarSensorOptions options) : base(options) { }


        protected override double GetBarData() => base.GetBarData() / MbDivisor;
    }
}
