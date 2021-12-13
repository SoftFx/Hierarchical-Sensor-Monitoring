using System;
using System.Text.Json;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;
using HSMSensorDataObjects.TypedDataObject;

namespace HSMServer.Core.Extensions
{
    public static class ConvertSensorValuesToDataEntitiesExtensions
    {
        public static SensorDataEntity Convert(this BoolSensorValue sensorValue, DateTime timeCollected,
            SensorStatus validationStatus = SensorStatus.Unknown)
        {
            var dataEntity = CreateSensorDataEntity(sensorValue, timeCollected, validationStatus);

            dataEntity.DataType = (byte)SensorType.BooleanSensor;
            dataEntity.TypedData = JsonSerializer.Serialize(
                new BoolSensorData()
                {
                    BoolValue = sensorValue.BoolValue,
                    Comment = sensorValue.Comment,
                });

            return dataEntity;
        }

        public static SensorDataEntity Convert(this IntSensorValue sensorValue, DateTime timeCollected,
            SensorStatus validationStatus = SensorStatus.Unknown)
        {
            var dataEntity = CreateSensorDataEntity(sensorValue, timeCollected, validationStatus);

            dataEntity.DataType = (byte)SensorType.IntSensor;
            dataEntity.TypedData = JsonSerializer.Serialize(
                new IntSensorData()
                {
                    IntValue = sensorValue.IntValue,
                    Comment = sensorValue.Comment,
                });

            return dataEntity;
        }

        public static SensorDataEntity Convert(this DoubleSensorValue sensorValue, DateTime timeCollected,
            SensorStatus validationStatus = SensorStatus.Unknown)
        {
            var dataEntity = CreateSensorDataEntity(sensorValue, timeCollected, validationStatus);

            dataEntity.DataType = (byte)SensorType.DoubleSensor;
            dataEntity.TypedData = JsonSerializer.Serialize(
                new DoubleSensorData()
                {
                    DoubleValue = sensorValue.DoubleValue,
                    Comment = sensorValue.Comment,
                });

            return dataEntity;
        }

        public static SensorDataEntity Convert(this StringSensorValue sensorValue, DateTime timeCollected,
            SensorStatus validationStatus = SensorStatus.Unknown)
        {
            var dataEntity = CreateSensorDataEntity(sensorValue, timeCollected, validationStatus);

            dataEntity.DataType = (byte)SensorType.StringSensor;
            dataEntity.TypedData = JsonSerializer.Serialize(
                new StringSensorData()
                {
                    StringValue = sensorValue.StringValue,
                    Comment = sensorValue.Comment,
                });

            return dataEntity;
        }

        public static SensorDataEntity Convert(this IntBarSensorValue sensorValue, DateTime timeCollected,
            SensorStatus validationStatus = SensorStatus.Unknown)
        {
            var dataEntity = CreateSensorDataEntity(sensorValue, timeCollected, validationStatus);

            dataEntity.DataType = (byte)SensorType.IntegerBarSensor;
            dataEntity.TypedData = JsonSerializer.Serialize(
                new IntBarSensorData()
                {
                    Max = sensorValue.Max,
                    Min = sensorValue.Min,
                    Mean = sensorValue.Mean,
                    LastValue = sensorValue.LastValue,
                    Count = sensorValue.Count,
                    Comment = sensorValue.Comment,
                    StartTime = sensorValue.StartTime.ToUniversalTime(),
                    EndTime = (sensorValue.EndTime == DateTime.MinValue ? DateTime.Now : sensorValue.EndTime).ToUniversalTime(),
                    Percentiles = sensorValue.Percentiles,
                });

            return dataEntity;
        }

        public static SensorDataEntity Convert(this DoubleBarSensorValue sensorValue, DateTime timeCollected,
            SensorStatus validationStatus = SensorStatus.Unknown)
        {
            var dataEntity = CreateSensorDataEntity(sensorValue, timeCollected, validationStatus);

            dataEntity.DataType = (byte)SensorType.DoubleBarSensor;
            dataEntity.TypedData = JsonSerializer.Serialize(
                new DoubleBarSensorData()
                {
                    Max = sensorValue.Max,
                    Min = sensorValue.Min,
                    Mean = sensorValue.Mean,
                    LastValue = sensorValue.LastValue,
                    Count = sensorValue.Count,
                    Comment = sensorValue.Comment,
                    StartTime = sensorValue.StartTime.ToUniversalTime(),
                    EndTime = (sensorValue.EndTime == DateTime.MinValue ? DateTime.Now : sensorValue.EndTime).ToUniversalTime(),
                    Percentiles = sensorValue.Percentiles,
                });

            return dataEntity;
        }

        public static SensorDataEntity Convert(this FileSensorBytesValue sensorValue, DateTime timeCollected,
            SensorStatus validationStatus = SensorStatus.Unknown)
        {
            var dataEntity = CreateSensorDataEntity(sensorValue, timeCollected, validationStatus);

            dataEntity.DataType = (byte)SensorType.FileSensorBytes;
            dataEntity.TypedData = JsonSerializer.Serialize(
                new FileSensorBytesData()
                {
                    Comment = sensorValue.Comment,
                    Extension = sensorValue.Extension,
                    FileContent = sensorValue.FileContent,
                    FileName = sensorValue.FileName,
                });

            return dataEntity;
        }

        public static SensorDataEntity Convert(this FileSensorValue sensorValue, DateTime timeCollected,
            SensorStatus validationStatus = SensorStatus.Unknown)
        {
            var dataEntity = CreateSensorDataEntity(sensorValue, timeCollected, validationStatus);

            dataEntity.DataType = (byte)SensorType.FileSensor;
            dataEntity.TypedData = JsonSerializer.Serialize(
                new FileSensorData()
                {
                    Comment = sensorValue.Comment,
                    Extension = sensorValue.Extension,
                    FileContent = sensorValue.FileContent,
                    FileName = sensorValue.FileName,
                });

            return dataEntity;
        }

        private static SensorDataEntity CreateSensorDataEntity(SensorValueBase sensorValue, DateTime timeCollected, SensorStatus validationStatus) =>
            new()
            {
                Path = sensorValue.Path,
                Status = (byte)sensorValue.Status.GetWorst(validationStatus),
                Time = sensorValue.Time.ToUniversalTime(),
                TimeCollected = timeCollected.ToUniversalTime(),
                Timestamp = GetTimestamp(sensorValue.Time),
            };

        private static long GetTimestamp(DateTime dateTime)
        {
            var timeSpan = dateTime - DateTime.UnixEpoch;
            return (long)timeSpan.TotalSeconds;
        }
    }
}
