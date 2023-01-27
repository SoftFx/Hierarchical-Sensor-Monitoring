using HSMDataCollector.Options;
using System.Diagnostics;

namespace HSMDataCollector.DefaultSensors.Unix
{
    internal sealed class UnixProcessCpu : BarMonitoringSensorBase<DoubleMonitoringBar, double>
    {
        private readonly Process _currentProcess = ProcessInfo.CurrentProcess;

        protected override string SensorName => "Process CPU";


        internal UnixProcessCpu(BarSensorOptions options) : base(options) { }


        protected override double GetBarData() =>
            100.0 * _currentProcess.PrivilegedProcessorTime.TotalMilliseconds / _currentProcess.TotalProcessorTime.TotalMilliseconds;
    }
}
