using System.Diagnostics;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsProcessMemory : WindowsSensorBase
    {
        private const int MbDivisor = 1 << 20;


        protected override string CategoryName => "Process";

        protected override string CounterName => "Working set";

        protected override string InstanceName { get; } = Process.GetCurrentProcess().ProcessName;


        protected override string SensorName => "Process memory MB";


        internal WindowsProcessMemory(string nodePath) : base(nodePath) { }


        protected override double GetBarData() => base.GetBarData() / MbDivisor;
    }
}
