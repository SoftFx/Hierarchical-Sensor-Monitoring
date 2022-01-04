using System;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Model.Sensor;

namespace HSMServer.Core.Converters
{
    public static class SensorValuesToSensorDatasExtensions
    {
        public static SensorData Convert(this SensorValueBase sensorValue, string productName,
            DateTime timeCollected, TransactionType transactionType) =>
            new()
            {
                Path = sensorValue.Path,
                Description = sensorValue.Description,
                Status = sensorValue.Status,
                Key = sensorValue.Key,
                Product = productName,
                Time = timeCollected,
                TransactionType = transactionType,
                SensorType = SensorTypeFactory.GetSensorType(sensorValue),
                StringValue = SensorDataPropertiesBuilder.GetStringValue(sensorValue, timeCollected),
                ShortStringValue = SensorDataPropertiesBuilder.GetShortStringValue(sensorValue),
            };
    }
}
