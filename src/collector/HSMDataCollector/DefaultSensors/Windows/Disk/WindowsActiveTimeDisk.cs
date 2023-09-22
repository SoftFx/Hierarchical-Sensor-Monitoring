using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal class WindowsActiveTimeDisk : WindowsDiskBarSensorBase
    {
        protected override string CounterName => "% Disk Time";


        public WindowsActiveTimeDisk(DiskBarSensorOptions options) : base(options) { }
    }
}