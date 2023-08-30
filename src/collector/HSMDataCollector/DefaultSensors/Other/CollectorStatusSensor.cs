using HSMDataCollector.Client;
using HSMDataCollector.Core;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;

namespace HSMDataCollector.DefaultSensors.Other
{
    internal sealed class CollectorStatusSensor : SensorBase<string>
    {
        public CollectorStatusSensor(InstantSensorOptions options) : base(options) { }


        public void BuildAndSendValue(HsmHttpsClient client, CollectorStatus collectorStatus, string error)
        {
            var dataStatus = string.IsNullOrEmpty(error) ? SensorStatus.Ok : SensorStatus.Error;

            client.Data.SendRequest(new StringSensorValue
            {
                Path = SensorPath,
                Value = $"{collectorStatus}",
            }.Complete(error, dataStatus));
        }
    }
}