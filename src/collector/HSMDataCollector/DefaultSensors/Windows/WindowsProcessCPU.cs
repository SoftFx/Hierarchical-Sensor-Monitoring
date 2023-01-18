using System;
using System.Diagnostics;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsProcessCPU : WindowsSensorBase
    {
        protected override string CategoryName => "Process";

        protected override string CounterName => "% Processor Time";

        protected override string InstanceName { get; } = Process.GetCurrentProcess().ProcessName;


        internal override string SensorName => "Process CPU";

        internal override TimeSpan CollectBarPeriod { get; } = TimeSpan.FromSeconds(5);

        internal override TimeSpan ReceiveDataPeriod { get; } = TimeSpan.FromMinutes(5);


        internal WindowsProcessCPU(string nodePath) : base(nodePath) { }
    }
}
