using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Diagnostic
{
    internal sealed class QueueOverflowSensor : IntQueueInfoSensor
    {
        public QueueOverflowSensor(BarSensorOptions options) : base(options) { }
    }
}