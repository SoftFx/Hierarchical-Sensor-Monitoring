using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects;
using HSMSensorDataObjects.BarData;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Extensions;
using HSMServer.Core.Helpers;
using System;
using System.Text.Json;

namespace HSMServer.Core.Converters
{
    public static class SensorValuesExtensions
    {
        public static SensorDataEntity Convert(this SensorValueBase sensorValue, DateTime timeCollected,
            SensorStatus validationStatus = SensorStatus.Unknown) =>
            new()
            {
                Path = sensorValue.Path,
                //Status = (byte)sensorValue.Status.GetWorst(validationStatus), // TODO this method won't be exist? bexause SensorValueBase = BaseValue = SensorDataEntity
                Time = sensorValue.Time.ToUniversalTime(),
                TimeCollected = timeCollected.ToUniversalTime(),
                Timestamp = GetTimestamp(sensorValue.Time),
                TypedData = TypedDataFactory.GetTypedData(sensorValue),
                DataType = (byte)SensorTypeFactory.GetSensorType(sensorValue),
            };

        //public static SensorDataEntity ConvertWithContentCompression(this FileSensorBytesValue sensorValue, DateTime timeCollected,
        //    SensorStatus validationStatus)
        //{
        //    int originalSize = sensorValue.FileContent.Length;

        //    var dataEntity = sensorValue.CompressContent().Convert(timeCollected, validationStatus);
        //    dataEntity.OriginalFileSensorContentSize = originalSize;

        //    return dataEntity;
        //}

        public static BarSensorValueBase Convert(this UnitedSensorValue value) =>
            BuildBarSensorValue(value)?.FillBarSensorValueCommonSettings(value);

        internal static long GetTimestamp(DateTime dateTime)
        {
            var timeSpan = dateTime - DateTime.UnixEpoch;
            return (long)timeSpan.TotalSeconds;
        }


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
