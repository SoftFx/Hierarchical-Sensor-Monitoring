using System;
using System.Text.Json;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMSensorDataObjects;
using HSMSensorDataObjects.TypedDataObject;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal static class SensorDataEntitiesFactory
    {
        internal static SensorDataEntity BuildSensorDataEntity(SensorType sensorType, bool withComment = true) =>
            sensorType switch
            {
                SensorType.BooleanSensor => BuildBoolSensorDataEntity(withComment),
                SensorType.IntSensor => BuildIntSensorDataEntity(withComment),
                SensorType.DoubleSensor => BuildDoubleSensorDataEntity(withComment),
                SensorType.StringSensor => BuildStringSensorDataEntity(withComment),
                SensorType.IntegerBarSensor => BuildIntBarSensorDataEntity(withComment),
                SensorType.DoubleBarSensor => BuildDoubleBarSensorDataEntity(withComment),
                SensorType.FileSensorBytes => BuildFileBytesSensorDataEntity(withComment),
                SensorType.FileSensor => BuildFileSensorDataEntity(withComment),
                _ => null,
            };

        private static SensorDataEntity BuildBoolSensorDataEntity(bool withComment)
        {
            var dataEntity = BuildSensorDataEntity(DateTime.UtcNow);

            dataEntity.DataType = (byte)SensorType.BooleanSensor;
            dataEntity.TypedData = JsonSerializer.Serialize(
                new BoolSensorData()
                {
                    BoolValue = RandomValuesGenerator.GetRandomBool(),
                    Comment = withComment ? RandomValuesGenerator.GetRandomString() : null,
                });

            return dataEntity;
        }

        private static SensorDataEntity BuildIntSensorDataEntity(bool withComment)
        {
            var dataEntity = BuildSensorDataEntity(DateTime.UtcNow);

            dataEntity.DataType = (byte)SensorType.IntSensor;
            dataEntity.TypedData = JsonSerializer.Serialize(
                new IntSensorData()
                {
                    IntValue = RandomValuesGenerator.GetRandomInt(),
                    Comment = withComment ? RandomValuesGenerator.GetRandomString() : null,
                });

            return dataEntity;
        }

        private static SensorDataEntity BuildDoubleSensorDataEntity(bool withComment)
        {
            var dataEntity = BuildSensorDataEntity(DateTime.UtcNow);

            dataEntity.DataType = (byte)SensorType.DoubleSensor;
            dataEntity.TypedData = JsonSerializer.Serialize(
                new DoubleSensorData()
                {
                    DoubleValue = RandomValuesGenerator.GetRandomDouble(),
                    Comment = withComment ? RandomValuesGenerator.GetRandomString() : null,
                });

            return dataEntity;
        }

        private static SensorDataEntity BuildStringSensorDataEntity(bool withComment)
        {
            var dataEntity = BuildSensorDataEntity(DateTime.UtcNow);

            dataEntity.DataType = (byte)SensorType.StringSensor;
            dataEntity.TypedData = JsonSerializer.Serialize(
                new StringSensorData()
                {
                    StringValue = RandomValuesGenerator.GetRandomString(),
                    Comment = withComment ? RandomValuesGenerator.GetRandomString() : null,
                });

            return dataEntity;
        }

        private static SensorDataEntity BuildIntBarSensorDataEntity(bool withComment)
        {
            var dataEntity = BuildSensorDataEntity(DateTime.UtcNow);

            dataEntity.DataType = (byte)SensorType.IntegerBarSensor;
            dataEntity.TypedData = JsonSerializer.Serialize(
                new IntBarSensorData()
                {
                    Min = RandomValuesGenerator.GetRandomInt(),
                    Max = RandomValuesGenerator.GetRandomInt(),
                    Mean = RandomValuesGenerator.GetRandomInt(),
                    LastValue = RandomValuesGenerator.GetRandomInt(),
                    Count = RandomValuesGenerator.GetRandomInt(positive: true),
                    StartTime = DateTime.UtcNow.AddMinutes(-5),
                    EndTime = DateTime.UtcNow.AddMinutes(5),
                    Comment = withComment ? RandomValuesGenerator.GetRandomString() : null,
                });

            return dataEntity;
        }

        private static SensorDataEntity BuildDoubleBarSensorDataEntity(bool withComment)
        {
            var dataEntity = BuildSensorDataEntity(DateTime.UtcNow);

            dataEntity.DataType = (byte)SensorType.DoubleBarSensor;
            dataEntity.TypedData = JsonSerializer.Serialize(
                new DoubleBarSensorData()
                {
                    Min = RandomValuesGenerator.GetRandomDouble(),
                    Max = RandomValuesGenerator.GetRandomDouble(),
                    Mean = RandomValuesGenerator.GetRandomDouble(),
                    LastValue = RandomValuesGenerator.GetRandomDouble(),
                    Count = RandomValuesGenerator.GetRandomInt(positive: true),
                    StartTime = DateTime.UtcNow.AddMinutes(-5),
                    EndTime = DateTime.UtcNow.AddMinutes(5),
                    Comment = withComment ? RandomValuesGenerator.GetRandomString() : null,
                });

            return dataEntity;
        }

        private static SensorDataEntity BuildFileBytesSensorDataEntity(bool withComment)
        {
            var dataEntity = BuildSensorDataEntity(DateTime.UtcNow);

            dataEntity.DataType = (byte)SensorType.FileSensorBytes;
            dataEntity.TypedData = JsonSerializer.Serialize(
                new FileSensorBytesData()
                {
                    Extension = RandomValuesGenerator.GetRandomString(3),
                    FileContent = RandomValuesGenerator.GetRandomBytes(),
                    FileName = nameof(FileSensorBytesData),
                    Comment = withComment ? RandomValuesGenerator.GetRandomString() : null,
                });

            return dataEntity;
        }

        private static SensorDataEntity BuildFileSensorDataEntity(bool withComment)
        {
            var dataEntity = BuildSensorDataEntity(DateTime.UtcNow);

            dataEntity.DataType = (byte)SensorType.FileSensor;
            dataEntity.TypedData = JsonSerializer.Serialize(
                new FileSensorData()
                {
                    Extension = RandomValuesGenerator.GetRandomString(3),
                    FileContent = RandomValuesGenerator.GetRandomString(),
                    FileName = nameof(FileSensorData),
                    Comment = withComment ? RandomValuesGenerator.GetRandomString() : null,
                });

            return dataEntity;
        }

        private static SensorDataEntity BuildSensorDataEntity(DateTime dateTime) =>
            new()
            {
                Time = dateTime.AddMinutes(-5),
                Timestamp = GetTimestamp(dateTime),
                Path = nameof(SensorDataEntity),
                TimeCollected = dateTime,
                Status = (byte)RandomValuesGenerator.GetRandomInt(0, 4),
            };

        private static long GetTimestamp(DateTime dateTime)
        {
            var timeSpan = (dateTime - DateTime.UnixEpoch);
            return (long)timeSpan.TotalSeconds;
        }
    }
}
