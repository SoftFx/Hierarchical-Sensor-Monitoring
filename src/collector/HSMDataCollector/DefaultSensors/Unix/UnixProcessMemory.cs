using HSMDataCollector.DefaultSensors.MonitoringSensor;
using System.Diagnostics;

namespace HSMDataCollector.DefaultSensors.Unix
{
    internal sealed class UnixProcessMemory : BarMonitoringSensorBase<DoubleMonitoringBar, double>
    {
        private const int MbDivisor = 1 << 20;

        private readonly Process _currentProcess = Process.GetCurrentProcess();


        protected override string SensorName => "Process memory MB";


        internal UnixProcessMemory(string nodePath) : base(nodePath) { }


        protected override double GetBarData() => _currentProcess.WorkingSet64 / MbDivisor;
    }
}
