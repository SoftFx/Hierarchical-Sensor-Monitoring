using HSMDataCollector.Options;
using HSMDataCollector.SyncQueue.BaseQueue;

namespace HSMDataCollector.DefaultSensors.Diagnostic
{
    internal sealed class PackageDataCountSensor : IntQueueInfoSensor
    {
        public PackageDataCountSensor(BarSensorOptions options) : base(options) { }


        internal void AddValue(string queueName, PackageInfo info) => AddValue(queueName, info.ValuesCount);
    }
}