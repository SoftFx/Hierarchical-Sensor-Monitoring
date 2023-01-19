using HSMDataCollector.DefaultSensors.MonitoringSensor;
using System.Diagnostics;

namespace HSMDataCollector.DefaultSensors.Unix
{
    internal sealed class UnixProcessCPU : BarMonitoringSensorBase<DoubleMonitoringBar, double>
    {
        internal override string SensorName => "Process CPU";


        internal UnixProcessCPU(string nodePath) : base(nodePath) { }


        protected override double GetBarData()
        {
            Process currentProcess = Process.GetCurrentProcess();

            return 100.0 * currentProcess.PrivilegedProcessorTime.TotalMilliseconds / currentProcess.TotalProcessorTime.TotalMilliseconds;
        }
    }
}
