using HSMDataCollector.DefaultSensors.MonitoringSensor;
using HSMDataCollector.Options;
using System.Diagnostics;

namespace HSMDataCollector.DefaultSensors.Unix
{
    internal sealed class UnixProcessThreadCount : BarMonitoringSensorBase<DoubleMonitoringBar, double>
    {
        private readonly Process _currentProcess = Process.GetCurrentProcess();

        protected override string SensorName => "Process thread count";


        internal UnixProcessThreadCount(BarSensorOptions options) : base(options) { }


        protected override double GetBarData() => _currentProcess.Threads.Count;
    }
}
