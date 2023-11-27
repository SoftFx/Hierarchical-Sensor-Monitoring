using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Diagnostic
{
    internal sealed class QueueOverflowSensor : BaseQueueInfoIntSensor
    {
        public QueueOverflowSensor(BarSensorOptions options) : base(options) { }
    }
}