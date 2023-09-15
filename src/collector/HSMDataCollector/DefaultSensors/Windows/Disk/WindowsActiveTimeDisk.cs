using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal class WindowsActiveTimeDisk : WindowsSensorBase
    {
        public const string Counter = "% Disk Time";


        protected override string CategoryName => Category;

        protected override string CounterName => Counter;

        protected override string InstanceName { get; }


        public WindowsActiveTimeDisk(DiskBarSensorOptions options) : base(options)
        {
            InstanceName = options.DiskInfo.DiskLetter;
        }
    }
}