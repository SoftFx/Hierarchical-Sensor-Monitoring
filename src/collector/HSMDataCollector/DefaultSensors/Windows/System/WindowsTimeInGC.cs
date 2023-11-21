using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsTimeInGC : WindowsSensorBase
    {
        protected override string CategoryName => ".NET CLR Memory";

        protected override string CounterName => "% Time in GC";

        protected override string InstanceName => "_Global_";



        internal WindowsTimeInGC(BarSensorOptions options) : base(options) { }
    }
}
