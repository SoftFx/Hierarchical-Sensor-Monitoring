using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal class WindowsActiveTimeDisk : WindowsDiskBarSensorBase
    {
        protected override string CounterName => "% Disk Time";

        protected override string InstanceName { get; }


        public WindowsActiveTimeDisk(DiskBarSensorOptions options) : base(options)
        {
            InstanceName = options.DiskInfo.DiskLetter;
        }
    }
}