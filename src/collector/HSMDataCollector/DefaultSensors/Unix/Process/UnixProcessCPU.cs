using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using System;
using System.Diagnostics;

namespace HSMDataCollector.DefaultSensors.Unix
{
    internal sealed class UnixProcessCpu : BarMonitoringSensorBase<DoubleMonitoringBar, double>
    {
        private readonly Process _currentProcess = ProcessInfo.CurrentProcess;

        private TimeSpan _startCpuUsage;
        private DateTime _startTime;

        protected override string SensorName => "Process CPU";


        internal UnixProcessCpu(BarSensorOptions options) : base(options)
        {
            InitStartingPoint();
        }


        protected override double GetBarData()
        {
            var endCpuUsage = _currentProcess.TotalProcessorTime;
            var endTime = DateTime.UtcNow;

            var cpuUsedMs = (endCpuUsage - _startCpuUsage).TotalMilliseconds;
            var totalMsPassed = (endTime - _startTime).TotalMilliseconds;

            InitStartingPoint();

            return (cpuUsedMs / totalMsPassed).ToPercent();
        }

        private void InitStartingPoint()
        {
            _startCpuUsage = _currentProcess.TotalProcessorTime;
            _startTime = DateTime.UtcNow;
        }
    }
}
