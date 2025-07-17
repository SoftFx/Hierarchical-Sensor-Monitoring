using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    public sealed class WindowsFreeRamMemory : WindowsSensorBase
    {
        protected override string CategoryName => "Memory";

        protected override string CounterName => "Available MBytes";



        internal WindowsFreeRamMemory(BarSensorOptions options) : base(options) { }
    }
}
