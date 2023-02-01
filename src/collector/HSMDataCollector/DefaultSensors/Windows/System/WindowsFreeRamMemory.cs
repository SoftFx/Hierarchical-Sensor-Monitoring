using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsFreeRamMemory : WindowsSensorBase
    {
        protected override string SensorName => "Free RAM memory MB";

        protected override string CategoryName => "Memory";

        protected override string CounterName => "Available MBytes";



        internal WindowsFreeRamMemory(BarSensorOptions options) : base(options) { }
    }
}
