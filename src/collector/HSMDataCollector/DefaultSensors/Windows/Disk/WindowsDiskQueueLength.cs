using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Windows
{
    public class WindowsDiskQueueLength : WindowsDiskBarSensorBase
    {
        public const string Counter = "Avg. Disk Queue Length";


        protected override string CounterName => Counter;


        internal WindowsDiskQueueLength(DiskBarSensorOptions options) : base(options) { }
    }
}