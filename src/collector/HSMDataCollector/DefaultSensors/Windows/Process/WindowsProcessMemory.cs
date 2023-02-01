using HSMDataCollector.Extensions;
using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsProcessMemory : WindowsSensorBase
    {
        protected override string SensorName => "Process memory MB";

        protected override string CategoryName => "Process";

        protected override string CounterName => "Working set";

        protected override string InstanceName { get; } = ProcessInfo.CurrentProcessName;



        internal WindowsProcessMemory(BarSensorOptions options) : base(options) { }


        protected override double GetBarData() => base.GetBarData().ToMegabytes();
    }
}
