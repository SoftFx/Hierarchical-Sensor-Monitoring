using HSMCommon.Model;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal static class SensorValuesFactory
    {
        internal static BaseValue BuildValue(SensorType sensorType) =>
            sensorType switch
            {
                SensorType.Boolean => BuildBooleanValue(),
                SensorType.Integer => BuildIntegerValue(),
                SensorType.Double => BuildDoubleValue(),
                SensorType.Rate => BuildRateValue(),
                SensorType.String => BuildStringValue(),
                SensorType.IntegerBar => BuildIntegerBarValue(),
                SensorType.DoubleBar => BuildDoubleBarValue(),
                SensorType.File => BuildFileValue(),
                SensorType.TimeSpan => BuildTimeSpanValue(),
                SensorType.Version => BuildVersionValue(),
                SensorType.Enum => BuildEnumValue(),
                _ => null,
            };

        internal static SensorValueBase BuildSensorValue(SensorType sensorType, string path, DateTime time) =>
            sensorType switch
            {
                SensorType.Boolean => BuildBooleanSensorValue(path, time),
                SensorType.Integer => BuildIntegerSensorValue(path, time),
                SensorType.Double => BuildDoubleSensorValue(path, time),
                SensorType.Rate => BuildRateSensorValue(path, time),
                SensorType.String => BuildStringSensorValue(path, time),
                SensorType.IntegerBar => BuildIntegerBarSensorValue(path, time),
                SensorType.DoubleBar => BuildDoubleBarSensorValue(path, time),
                SensorType.File => BuildFileSensorValue(path, time),
                SensorType.TimeSpan => BuildTimeSpanSensorValue(path, time),
                SensorType.Version => BuildVersionSensorValue(path, time),
                SensorType.Enum => BuildEnumSensorValue(path, time),
                _ => null,
            };

        internal static BooleanValue BuildBooleanValue() =>
            new()
            {
                Comment = RandomGenerator.GetRandomString(),
                Time = DateTime.UtcNow,
                Status = SensorStatus.Ok,
                Value = RandomGenerator.GetRandomBool(),
            };

        internal static BoolSensorValue BuildBooleanSensorValue(string path, DateTime time) =>
            new()
            {
                Comment = time.Microsecond.ToString(),
                Time = time,
                Status = HSMSensorDataObjects.SensorStatus.Ok,
                Value = RandomGenerator.GetRandomBool(),
                Path = path,
             };

        internal static IntegerValue BuildIntegerValue() =>
            new()
            {
                Comment = RandomGenerator.GetRandomString(),
                Time = DateTime.UtcNow,
                Status = SensorStatus.Ok,
                Value = RandomGenerator.GetRandomInt(),
            };

        internal static IntSensorValue BuildIntegerSensorValue(string path, DateTime time) =>
            new()
            {
                Comment = RandomGenerator.GetRandomString(),
                Time = time,
                Status = HSMSensorDataObjects.SensorStatus.Ok,
                Value = RandomGenerator.GetRandomInt(),
                Path = path,
            };

        internal static DoubleValue BuildDoubleValue() =>
            new()
            {
                Comment = RandomGenerator.GetRandomString(),
                Time = DateTime.UtcNow,
                Status = SensorStatus.Ok,
                Value = RandomGenerator.GetRandomDouble(),
            };

        internal static DoubleSensorValue BuildDoubleSensorValue(string path, DateTime time) =>
            new()
            {
                Comment = RandomGenerator.GetRandomString(),
                Time = time,
                Status = HSMSensorDataObjects.SensorStatus.Ok,
                Value = RandomGenerator.GetRandomDouble(),
                Path = path
            };

        internal static RateValue BuildRateValue() =>
            new()
            {
                Comment = RandomGenerator.GetRandomString(),
                Time = DateTime.UtcNow,
                Status = SensorStatus.Ok,
                Value = RandomGenerator.GetRandomDouble(),
            };

        internal static RateSensorValue BuildRateSensorValue(string path, DateTime time) =>
            new()
            {
                Comment = RandomGenerator.GetRandomString(),
                Time = time,
                Status = HSMSensorDataObjects.SensorStatus.Ok,
                Value = RandomGenerator.GetRandomDouble(),
                Path = path
            };

        internal static StringValue BuildStringValue() =>
            new()
            {
                Comment = RandomGenerator.GetRandomString(),
                Time = DateTime.UtcNow,
                Status = SensorStatus.Ok,
                Value = RandomGenerator.GetRandomString(),
            };

        internal static StringSensorValue BuildStringSensorValue(string path, DateTime time) =>
            new()
            {
                Comment = RandomGenerator.GetRandomString(),
                Time = time,
                Status = HSMSensorDataObjects.SensorStatus.Ok,
                Value = RandomGenerator.GetRandomString(),
                Path = path
            };

        internal static TimeSpanValue BuildTimeSpanValue() =>
            new()
            {
                Comment = RandomGenerator.GetRandomString(),
                Time = DateTime.UtcNow,
                Status = SensorStatus.Ok,
                Value = RandomGenerator.GetRandomTimeSpan(),
            };

        internal static TimeSpanSensorValue BuildTimeSpanSensorValue(string path, DateTime time) =>
            new()
            {
                Comment = RandomGenerator.GetRandomString(),
                Time = time,
                Status = HSMSensorDataObjects.SensorStatus.Ok,
                Value = RandomGenerator.GetRandomTimeSpan(),
                Path = path
            };

        internal static VersionValue BuildVersionValue() =>
            new()
            {
                Comment = RandomGenerator.GetRandomString(),
                Time = DateTime.UtcNow,
                Status = SensorStatus.Ok,
                Value = new Version(RandomGenerator.GetRandomInt(positive: true), RandomGenerator.GetRandomInt(positive: true)),
            };

        internal static VersionSensorValue BuildVersionSensorValue(string path, DateTime time) =>
            new()
            {
                Comment = RandomGenerator.GetRandomString(),
                Time = time,
                Status = HSMSensorDataObjects.SensorStatus.Ok,
                Value = new Version(RandomGenerator.GetRandomInt(positive: true), RandomGenerator.GetRandomInt(positive: true)),
                Path = path
            };

        internal static IntegerBarValue BuildIntegerBarValue() =>
            new()
            {
                Comment = RandomGenerator.GetRandomString(),
                Time = DateTime.UtcNow,
                Status = SensorStatus.Ok,
                Count = RandomGenerator.GetRandomInt(positive: true),
                OpenTime = DateTime.UtcNow.AddSeconds(-10),
                CloseTime = DateTime.UtcNow.AddSeconds(10),
                Min = RandomGenerator.GetRandomInt(),
                Max = RandomGenerator.GetRandomInt(),
                Mean = RandomGenerator.GetRandomInt(),
                FirstValue = RandomGenerator.GetRandomInt(),
                LastValue = RandomGenerator.GetRandomInt(),
            };

        internal static IntBarSensorValue BuildIntegerBarSensorValue(string path, DateTime time) =>
            new()
            {
                Comment = RandomGenerator.GetRandomString(),
                Time = time,
                Status = HSMSensorDataObjects.SensorStatus.Ok,
                Count = RandomGenerator.GetRandomInt(positive: true),
                OpenTime = DateTime.UtcNow.AddSeconds(-10),
                CloseTime = DateTime.UtcNow.AddSeconds(10),
                Min = RandomGenerator.GetRandomInt(),
                Max = RandomGenerator.GetRandomInt(),
                Mean = RandomGenerator.GetRandomInt(),
                FirstValue = RandomGenerator.GetRandomInt(),
                LastValue = RandomGenerator.GetRandomInt(),
                Path = path
            };

        internal static DoubleBarValue BuildDoubleBarValue() =>
            new()
            {
                Comment = RandomGenerator.GetRandomString(),
                Time = DateTime.UtcNow,
                Status = SensorStatus.Ok,
                Count = RandomGenerator.GetRandomInt(positive: true),
                OpenTime = DateTime.UtcNow.AddSeconds(-10),
                CloseTime = DateTime.UtcNow.AddSeconds(10),
                Min = RandomGenerator.GetRandomDouble(),
                Max = RandomGenerator.GetRandomDouble(),
                Mean = RandomGenerator.GetRandomDouble(),
                FirstValue = RandomGenerator.GetRandomDouble(),
                LastValue = RandomGenerator.GetRandomDouble(),
            };

        internal static DoubleBarSensorValue BuildDoubleBarSensorValue(string path, DateTime time) =>
            new()
            {
                Comment = RandomGenerator.GetRandomString(),
                Time = time,
                Status = HSMSensorDataObjects.SensorStatus.Ok,
                Count = RandomGenerator.GetRandomInt(positive: true),
                OpenTime = DateTime.UtcNow.AddSeconds(-10),
                CloseTime = DateTime.UtcNow.AddSeconds(10),
                Min = RandomGenerator.GetRandomDouble(),
                Max = RandomGenerator.GetRandomDouble(),
                Mean = RandomGenerator.GetRandomDouble(),
                FirstValue = RandomGenerator.GetRandomDouble(),
                LastValue = RandomGenerator.GetRandomDouble(),
                Path = path
            };

        internal static FileValue BuildFileValue()
        {
            var fileContent = RandomGenerator.GetRandomBytes();

            return new()
            {
                Comment = RandomGenerator.GetRandomString(),
                Time = DateTime.UtcNow,
                Status = SensorStatus.Ok,
                Value = fileContent,
                Extension = RandomGenerator.GetRandomString(3),
                Name = nameof(FileValue),
                OriginalSize = fileContent.LongLength,
            };
        }

        internal static FileSensorValue BuildFileSensorValue(string path, DateTime time)
        {
            var fileContent = RandomGenerator.GetRandomBytes();

            return new()
            {
                Comment = RandomGenerator.GetRandomString(),
                Time = time,
                Status = HSMSensorDataObjects.SensorStatus.Ok,
                Value = [.. fileContent],
                Extension = RandomGenerator.GetRandomString(3),
                Name = nameof(FileValue),
                Path = path
            };
        }

        internal static EnumValue BuildEnumValue() =>
            new()
            {
                Comment = RandomGenerator.GetRandomString(),
                Time = DateTime.UtcNow,
                Status = SensorStatus.Ok,
                Value = RandomGenerator.GetRandomInt(),
            };

        internal static EnumSensorValue BuildEnumSensorValue(string path, DateTime time) =>
            new()
            {
                Comment = RandomGenerator.GetRandomString(),
                Time = time,
                Status = HSMSensorDataObjects.SensorStatus.Ok,
                Value = RandomGenerator.GetRandomInt(),
                Path = path
            };

        private static Dictionary<double, T> GetPercentileValues<T>(Func<T> getValue, int size = 2)
        {
            var percentiles = new Dictionary<double, T>(size);

            for (int i = 0; i < size; ++i)
                percentiles.Add(RandomGenerator.GetRandomDouble(), getValue());

            return percentiles;
        }
    }
}
