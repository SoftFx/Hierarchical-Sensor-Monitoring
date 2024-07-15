using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Unix
{
    internal sealed class UnixProcessThreadCount : CollectableBarMonitoringSensorBase<DoubleMonitoringBar, double>
    {
        internal UnixProcessThreadCount(BarSensorOptions options) : base(options) { }


        protected override double? GetBarData() => ProcessInfo.CurrentProcess.Threads.Count;
    }
}
