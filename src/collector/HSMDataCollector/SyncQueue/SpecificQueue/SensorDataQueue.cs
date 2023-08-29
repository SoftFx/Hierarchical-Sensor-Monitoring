using HSMDataCollector.Core;
using HSMDataCollector.Extensions;
using HSMSensorDataObjects.SensorValueRequests;

namespace HSMDataCollector.SyncQueue
{
    internal class SensorDataQueue : SyncQueue<SensorValueBase>, IValuesQueue
    {
        public SensorDataQueue(CollectorOptions options) : base(options, options.PackageCollectPeriod) { }


        public override void Push(SensorValueBase value) => Enqueue(_valuesQueue, value.TrimLongComment());


        protected override bool IsSendValue(SensorValueBase value)
        {
            switch (value)
            {
                case FileSensorValue fileValue:
                    InvokeNewValue(fileValue);
                    return false;

                case BarSensorValueBase barSensor when barSensor.Count == 0:
                    return false;
            }

            return true;
        }
    }
}