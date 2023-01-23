namespace HSMDataCollector.DefaultSensors.Windows
{
    internal sealed class WindowsFreeRamMemory : WindowsSensorBase
    {
        protected override string CategoryName => "Memory";

        protected override string CounterName => "Available MBytes";


        internal override string SensorName => "Free memory MB";


        internal WindowsFreeRamMemory(string nodePath) : base(nodePath) { }
    }
}
