using System;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Model.Sensor;

namespace HSMServer.Core.Converters
{
    public static class SensorValuesToSensorDatasExtensions
    {
        public static SensorData Convert(this SensorValueBase sensorValue, string productName, DateTime timeCollected, TransactionType type) =>
           sensorValue switch
           {
               BoolSensorValue => CreateSensorData(sensorValue, productName, timeCollected, type, SensorType.BooleanSensor),
               IntSensorValue => CreateSensorData(sensorValue, productName, timeCollected, type, SensorType.IntSensor),
               DoubleSensorValue => CreateSensorData(sensorValue, productName, timeCollected, type, SensorType.DoubleSensor),
               StringSensorValue => CreateSensorData(sensorValue, productName, timeCollected, type, SensorType.StringSensor),
               IntBarSensorValue => CreateSensorData(sensorValue, productName, timeCollected, type, SensorType.IntegerBarSensor),
               DoubleBarSensorValue => CreateSensorData(sensorValue, productName, timeCollected, type, SensorType.DoubleBarSensor),
               FileSensorBytesValue => CreateSensorData(sensorValue, productName, timeCollected, type, SensorType.FileSensorBytes),
               FileSensorValue => CreateSensorData(sensorValue, productName, timeCollected, type, SensorType.FileSensor),
               _ => null,
           };


        private static SensorData CreateSensorData(SensorValueBase sensorValue, string productName,
            DateTime timeCollected, TransactionType transactionType, SensorType sensorType) =>
            new()
            {
                Path = sensorValue.Path,
                Description = sensorValue.Description,
                Status = sensorValue.Status,
                Key = sensorValue.Key,
                Product = productName,
                Time = timeCollected,
                TransactionType = transactionType,
                SensorType = sensorType,
                StringValue = SensorDataPropertiesBuilder.GetStringValue(sensorValue, timeCollected),
                ShortStringValue = SensorDataPropertiesBuilder.GetShortStringValue(sensorValue),
            };
    }
}
