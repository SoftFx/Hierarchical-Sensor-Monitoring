using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Diagnostics;

namespace HSMDataCollector.DefaultSensors.Unix
{
    internal sealed class UnixProcessCPU : BarMonitoringSensorBase<double, DoubleBarSensorValue>
    {
        internal override string SensorName => "Process CPU";


        internal override TimeSpan ReceiveDataPeriod { get; } = TimeSpan.FromMinutes(5);

        internal override TimeSpan CollectBarPeriod { get; } = TimeSpan.FromSeconds(5);


        internal UnixProcessCPU(string nodePath) : base(nodePath) { }


        protected override double GetBarData()
        {
            Process currentProcess = Process.GetCurrentProcess();

            return 100.0 * currentProcess.PrivilegedProcessorTime.TotalMilliseconds / currentProcess.TotalProcessorTime.TotalMilliseconds;
        }
    }
}
