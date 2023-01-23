using System.Diagnostics;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsProcessCpu : WindowsSensorBase
    {
        protected override string CategoryName => "Process";

        protected override string CounterName => "% Processor Time";

        protected override string InstanceName { get; } = Process.GetCurrentProcess().ProcessName;


        internal override string SensorName => "Process CPU";


        internal WindowsProcessCpu(string nodePath) : base(nodePath) { }
    }
}
