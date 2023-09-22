using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal class WindowsDiskBarSensorBase : WindowsSensorBase
    {
        public const string Category = "PhysicalDisk";


        protected override string CategoryName => Category;

        protected override string CounterName { get; }

        protected override string InstanceName { get; }

        protected WindowsDiskBarSensorBase(BarSensorOptions options) : base(options) { }
    }
}