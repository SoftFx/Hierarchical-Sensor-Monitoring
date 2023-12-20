using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsProcessTimeInGC : WindowsTimeInGCBase
    {
        protected override string InstanceName => ProcessInfo.CurrentProcessName;


        internal WindowsProcessTimeInGC(BarSensorOptions options) : base(options) { }
    }
}
