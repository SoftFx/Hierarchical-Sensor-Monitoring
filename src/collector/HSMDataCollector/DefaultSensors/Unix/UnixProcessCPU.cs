using HSMDataCollector.DefaultSensors.MonitoringSensor;
using System.Diagnostics;

namespace HSMDataCollector.DefaultSensors.Unix
{
    internal sealed class UnixProcessCpu : BarMonitoringSensorBase<DoubleMonitoringBar, double>
    {
        private readonly Process _currentProcess = Process.GetCurrentProcess();

        protected override string SensorName => "Process CPU";


        internal UnixProcessCpu(string nodePath) : base(nodePath) { }


        protected override double GetBarData() =>
            100.0 * _currentProcess.PrivilegedProcessorTime.TotalMilliseconds / _currentProcess.TotalProcessorTime.TotalMilliseconds;
    }
}
