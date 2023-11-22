using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal abstract class WindowsTimeInGCBase : WindowsSensorBase
    {
        protected override string CategoryName => ".NET CLR Memory";

        protected override string CounterName => "% Time in GC";


        internal WindowsTimeInGCBase(BarSensorOptions options) : base(options) { }
    }
}
