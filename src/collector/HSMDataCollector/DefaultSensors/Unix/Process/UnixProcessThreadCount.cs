using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Unix
{
    internal sealed class UnixProcessThreadCount : BarMonitoringSensorBase<DoubleMonitoringBar, double>
    {
        protected override string SensorName => "Process thread count";


        internal UnixProcessThreadCount(BarSensorOptions options) : base(options) { }


        protected override double GetBarData() => ProcessInfo.CurrentProcess.Threads.Count;
    }
}
