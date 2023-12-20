using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    internal class WindowsDiskQueueLength : WindowsDiskBarSensorBase
    {
        public const string Counter = "Avg. Disk Queue Length";


        protected override string CounterName => Counter;


        public WindowsDiskQueueLength(DiskBarSensorOptions options) : base(options) { }
    }
}