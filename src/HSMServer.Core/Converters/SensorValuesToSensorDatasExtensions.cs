using System;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Model.Sensor;

namespace HSMServer.Core.Converters
{
    public static class SensorValuesToSensorDatasExtensions
    {
        public static SensorData Convert(this SensorValueBase sensorValue, string productName, DateTime timeCollected, TransactionType type) =>
           sensorValue switch
           {
               BoolSensorValue => CreateSensorData(sensorValue, productName, timeCollected, type),
               IntSensorValue => CreateSensorData(sensorValue, productName, timeCollected, type),
               DoubleSensorValue => CreateSensorData(sensorValue, productName, timeCollected, type),
               StringSensorValue => CreateSensorData(sensorValue, productName, timeCollected, type),
               IntBarSensorValue => CreateSensorData(sensorValue, productName, timeCollected, type),
               DoubleBarSensorValue => CreateSensorData(sensorValue, productName, timeCollected, type),
               FileSensorBytesValue => CreateSensorData(sensorValue, productName, timeCollected, type),
               FileSensorValue => CreateSensorData(sensorValue, productName, timeCollected, type),
               _ => null,
           };


        private static SensorData CreateSensorData(SensorValueBase sensorValue, string productName,
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
