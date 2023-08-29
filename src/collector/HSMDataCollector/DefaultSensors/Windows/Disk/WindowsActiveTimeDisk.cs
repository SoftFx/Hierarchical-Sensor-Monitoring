using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal class WindowsActiveTimeDisk : WindowsSensorBase
    {
        protected override string CategoryName => "PhysicalDisk";

        protected override string CounterName => "% Disk Time";

        protected override string InstanceName { get; }


        public WindowsActiveTimeDisk(DiskBarSensorOptions options) : base(options)
        {
            InstanceName = options.DiskInfo.DiskLetter;
        }
    }
}