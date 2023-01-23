using HSMDataCollector.DefaultSensors.MonitoringSensor;
using System.Diagnostics;

namespace HSMDataCollector.DefaultSensors.Unix
{
    internal sealed class UnixProcessThreadCount : BarMonitoringSensorBase<DoubleMonitoringBar, double>
    {
        internal override string SensorName => "Process thread count";


        internal UnixProcessThreadCount(string nodePath) : base(nodePath) { }


        protected override double GetBarData()
        {
            Process currentProcess = Process.GetCurrentProcess();

            return currentProcess.Threads.Count;
        }
    }
}
