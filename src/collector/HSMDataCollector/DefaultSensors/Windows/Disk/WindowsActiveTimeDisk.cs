using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal class WindowsActiveTimeDisk : WindowsDiskBarSensorBase
    {
        public const string Counter = "% Disk Time";


        protected override string CounterName => Counter;


        public WindowsActiveTimeDisk(DiskBarSensorOptions options) : base(options) { }
    }
}