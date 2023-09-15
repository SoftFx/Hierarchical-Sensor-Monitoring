using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal class WindowsDiskQueueLength : WindowsSensorBase
    {
        public const string Counter = "Avg. Disk Queue Length";


        protected override string CategoryName => Category;

        protected override string CounterName => Counter;

        protected override string InstanceName { get; }


        public WindowsDiskQueueLength(DiskBarSensorOptions options) : base(options)
        {
            InstanceName = $"{options.DiskInfo.DiskLetter}:";
        }
    }
}