using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal class WindowsDiskQueueLength : WindowsDiskBarSensorBase
    {
        protected override string CounterName => "Avg. Disk Queue Length";

        protected override string InstanceName { get; }


        public WindowsDiskQueueLength(DiskBarSensorOptions options) : base(options)
        {
            InstanceName = $"{options.DiskInfo.DiskLetter}:";
        }
    }
}