using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    public sealed class WindowsTimeInGC : WindowsTimeInGCBase
    {
        protected override string InstanceName => "_Global_";


        internal WindowsTimeInGC(BarSensorOptions options) : base(options) { }
    }
}
