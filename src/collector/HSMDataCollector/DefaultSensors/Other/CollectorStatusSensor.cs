using HSMDataCollector.Core;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;

namespace HSMDataCollector.DefaultSensors.Other
{
    internal sealed class CollectorStatusSensor : SensorBase<string>
    {
        protected override string SensorName => "Collector status";


        public CollectorStatusSensor(SensorOptions options) : base(options) { }


        public void BuildAndSendValue(HSMClient client, CollectorStatus collectorStatus, string error)
        {
            var dataStatus = string.IsNullOrEmpty(error) ? SensorStatus.Ok : SensorStatus.Error;

            client.SendData(new StringSensorValue
            {
                Path = SensorPath,
                Value = $"{collectorStatus}",
            }.Complete(error, dataStatus));
        }
    }
}