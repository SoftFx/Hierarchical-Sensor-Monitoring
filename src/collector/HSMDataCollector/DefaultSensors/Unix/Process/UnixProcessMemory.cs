using HSMDataCollector.Extensions;
using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Unix
{
    internal sealed class UnixProcessMemory : CollectableBarMonitoringSensorBase<DoubleMonitoringBar, double>
    {
        internal UnixProcessMemory(BarSensorOptions options) : base(options) { }


        protected override double GetBarData() => ProcessInfo.CurrentProcess.WorkingSet64.BytesToMegabytes();
    }
}
