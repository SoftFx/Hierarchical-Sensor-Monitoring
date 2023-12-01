using HSMDataCollector.Options;
using HSMDataCollector.SyncQueue.BaseQueue;

namespace HSMDataCollector.DefaultSensors.Diagnostic
{
    internal sealed class PackageDataAvrProcessTimeSensor : DoubleQueueInfoSensor
    {
        public PackageDataAvrProcessTimeSensor(BarSensorOptions options) : base(options) { }


        internal void AddValue(string queueName, PackageInfo info) => AddValue(queueName, info.AvrTimeInQueue);


        protected override double Apply(double oldValue, double newValue) => (oldValue + newValue) / 2.0;
    }
}