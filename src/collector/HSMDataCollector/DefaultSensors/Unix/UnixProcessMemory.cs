using HSMDataCollector.DefaultSensors.MonitoringSensor;
using System.Diagnostics;

namespace HSMDataCollector.DefaultSensors.Unix
{
    internal sealed class UnixProcessMemory : BarMonitoringSensorBase<DoubleMonitoringBar, double>
    {
        private const int MbDivisor = 1 << 20;


        internal override string SensorName => "Process memory MB";


        internal UnixProcessMemory(string nodePath) : base(nodePath) { }


        protected override double GetBarData()
        {
            Process currentProcess = Process.GetCurrentProcess();

            return currentProcess.WorkingSet64 / MbDivisor;
        }
    }
}
