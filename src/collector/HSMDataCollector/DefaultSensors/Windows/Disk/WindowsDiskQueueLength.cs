using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal class WindowsDiskQueueLength : WindowsDiskBarSensorBase
    {
        protected override string CounterName => "Avg. Disk Queue Length";


        public WindowsDiskQueueLength(DiskBarSensorOptions options) : base(options) { }
    }
}