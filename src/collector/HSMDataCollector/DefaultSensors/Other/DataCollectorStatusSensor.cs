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


        public void BuildAndSendValue(HSMClient client, CollectorStatus status, string error)
        {
            client.SendData(new StringSensorValue
            {
                Comment = error,
                Value = $"{status}",
                Status = string.IsNullOrEmpty(error) ? SensorStatus.Ok : SensorStatus.Error,
            });
        }
    }
}