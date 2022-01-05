using System;
using System.Text.Json;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects;
using HSMSensorDataObjects.BarData;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Extensions;
using HSMServer.Core.Model.Sensor;

namespace HSMServer.Core.Converters
{
    public static class SensorValuesExtensions
    {
        private const char SensorPathSeparator = '/';


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

        public static SensorDataEntity Convert(this SensorValueBase sensorValue, DateTime timeCollected,
            SensorStatus validationStatus = SensorStatus.Unknown) =>
            new()
            {
                Path = sensorValue.Path,
                Status = (byte)sensorValue.Status.GetWorst(validationStatus),
                Time = sensorValue.Time.ToUniversalTime(),
                TimeCollected = timeCollected.ToUniversalTime(),
                Timestamp = GetTimestamp(sensorValue.Time),
                TypedData = TypedDataFactory.GetTypedData(sensorValue),
                DataType = (byte)SensorTypeFactory.GetSensorType(sensorValue),
            };

        public static SensorInfo Convert(this SensorValueBase sensorValue, string productName) =>
            new()
            {
                Path = sensorValue.Path,
                Description = sensorValue.Description,
                ProductName = productName,
                SensorType = SensorTypeFactory.GetSensorType(sensorValue),
                SensorName = ExtractSensor(sensorValue.Path),
            };

        public static BarSensorValueBase Convert(this UnitedSensorValue value) =>
            BuildBarSensorValue(value)?.FillBarSensorValueCommonSettings(value);


        private static long GetTimestamp(DateTime dateTime)
        {
            var timeSpan = dateTime - DateTime.UnixEpoch;
            return (long)timeSpan.TotalSeconds;
        }

        private static string ExtractSensor(string path) => path?.Split(SensorPathSeparator)?[^1];


        private static BarSensorValueBase BuildBarSensorValue(UnitedSensorValue unitedSensorValue) =>
            unitedSensorValue.Type switch
            {
                SensorType.IntegerBarSensor => BuildIntBarSensorValue(unitedSensorValue),
                SensorType.DoubleBarSensor => BuildDoubleBarSensorValue(unitedSensorValue),
                _ => null,
            };

        private static IntBarSensorValue BuildIntBarSensorValue(UnitedSensorValue unitedSensorValue)
        {
            var data = JsonSerializer.Deserialize<IntBarData>(unitedSensorValue.Data);

            return new()
            {
                Max = data.Max,
                Mean = data.Mean,
                Min = data.Min,
                Percentiles = data.Percentiles,
                LastValue = data.LastValue,
                Count = data.Count,
                StartTime = data.StartTime,
                EndTime = data.EndTime,
            };
        }

        private static DoubleBarSensorValue BuildDoubleBarSensorValue(UnitedSensorValue unitedSensorValue)
        {
            var data = JsonSerializer.Deserialize<DoubleBarData>(unitedSensorValue.Data);

            return new()
            {
                Max = data.Max,
                Mean = data.Mean,
                Min = data.Min,
                Percentiles = data.Percentiles,
                LastValue = data.LastValue,
                Count = data.Count,
                StartTime = data.StartTime,
                EndTime = data.EndTime,
            };
        }

        private static BarSensorValueBase FillBarSensorValueCommonSettings(this BarSensorValueBase sensorValue, UnitedSensorValue unitedSensorValue)
        {
            sensorValue.Comment = unitedSensorValue.Comment;
            sensorValue.Path = unitedSensorValue.Path;
            sensorValue.Description = unitedSensorValue.Description;
            sensorValue.Status = unitedSensorValue.Status;
            sensorValue.Key = unitedSensorValue.Key;
            sensorValue.Time = unitedSensorValue.Time;

            return sensorValue;
        }
    }
}
