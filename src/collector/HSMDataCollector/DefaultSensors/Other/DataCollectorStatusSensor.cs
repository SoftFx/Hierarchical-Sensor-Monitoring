using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;

namespace HSMDataCollector.DefaultSensors.Other
{
    internal sealed class CollectorStatusSensor : SensorBase<string>
    {
        protected override string SensorName => "Collector statuses";


        public CollectorStatusSensor(SensorOptions options) : base(options) { }


        public void SendValue(CollectorStatus status, string error) =>
            SendValue(new StringSensorValue
            {
                Comment = error,
                Value = $"{status}",
                Status = string.IsNullOrEmpty(error) ? SensorStatus.Ok : SensorStatus.Error,
            });
    }
}