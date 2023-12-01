using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal abstract class WindowsDiskBarSensorBase : WindowsSensorBase
    {
        public const string Category = "LogicalDisk";


        protected override string CategoryName => Category;

        protected override string InstanceName { get; }


        protected WindowsDiskBarSensorBase(DiskBarSensorOptions options) : base(options)
        {
            InstanceName = $"{options.DiskInfo.DiskLetter}:";
        }
    }
}