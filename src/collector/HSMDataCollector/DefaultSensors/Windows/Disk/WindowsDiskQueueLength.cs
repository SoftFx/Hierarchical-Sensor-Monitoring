using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal class WindowsDiskQueueLength : WindowsSensorBase
    {
        protected override string CategoryName => "LogicalDisk";

        protected override string CounterName => "Avg. Disk Queue Length";

        protected override string InstanceName { get; }


        public WindowsDiskQueueLength(DiskBarSensorOptions options) : base(options)
        {
            InstanceName = $"{options.DiskInfo.DiskLetter}:";
        }
    }
}