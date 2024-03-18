using HSMDataCollector.Core;
using HSMDataCollector.Extensions;
using HSMSensorDataObjects.SensorValueRequests;

namespace HSMDataCollector.SyncQueue
{
    internal class SensorDataQueue : SyncQueue<SensorValueBase>
    {
        protected override string QueueName => "Sensor data";


        public SensorDataQueue(CollectorOptions options) : base(options, options.PackageCollectPeriod) { }


        protected override SensorValueBase CompressValue(SensorValueBase value) => value.TrimLongComment();

        protected override bool IsValidValue(SensorValueBase value)
        {
            switch (value)
            {
                case FileSensorValue fileValue:
                    Send(fileValue);
                    return false;

                case BarSensorValueBase barSensor when barSensor.Count == 0:
                    return false;
            }

            return true;
        }
    }
}