using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsFreeRamMemory : WindowsSensorBase
    {
        protected override string CategoryName => "Memory";

        protected override string CounterName => "Available MBytes";


        protected override string SensorName => "Free RAM memory MB";


        internal WindowsFreeRamMemory(BarSensorOptions options) : base(options) { }
    }
}
