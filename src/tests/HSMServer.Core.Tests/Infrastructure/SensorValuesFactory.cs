using HSMServer.Core.Model;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal static class SensorValuesFactory
    {
        internal static BaseValue BuildSensorValue(SensorType sensorType) =>
            sensorType switch
            {
                SensorType.Boolean => BuildBooleanValue(),
                SensorType.Integer => BuildIntegerValue(),
                SensorType.Double => BuildDoubleValue(),
                SensorType.String => BuildStringValue(),
                SensorType.IntegerBar => BuildIntegerBarValue(),
                SensorType.DoubleBar => BuildDoubleBarValue(),
                SensorType.File => BuildFileValue(),
                SensorType.TimeSpan => BuildTimeSpanValue(),
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

        internal static IntegerValue BuildIntegerValue() =>
            new()
            {
                Comment = RandomGenerator.GetRandomString(),
                Time = DateTime.UtcNow,
                Status = SensorStatus.Ok,
                Value = RandomGenerator.GetRandomInt(),
            };

        internal static DoubleValue BuildDoubleValue() =>
            new()
            {
                Comment = RandomGenerator.GetRandomString(),
                Time = DateTime.UtcNow,
                Status = SensorStatus.Ok,
                Value = RandomGenerator.GetRandomDouble(),
            };

        internal static StringValue BuildStringValue() =>
            new()
            {
                Comment = RandomGenerator.GetRandomString(),
                Time = DateTime.UtcNow,
                Status = SensorStatus.Ok,
                Value = RandomGenerator.GetRandomString(),
            };
        
        internal static TimeSpanValue BuildTimeSpanValue() =>
            new()
            {
                Comment = RandomGenerator.GetRandomString(),
                Time = DateTime.UtcNow,
                Status = SensorStatus.Ok,
                Value = RandomGenerator.GetRandomTimeSpan(),
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
                LastValue = RandomGenerator.GetRandomInt(),
                Percentiles = GetPercentileValues(() => RandomGenerator.GetRandomInt()),
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
                LastValue = RandomGenerator.GetRandomDouble(),
                Percentiles = GetPercentileValues(RandomGenerator.GetRandomDouble),
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


        private static Dictionary<double, T> GetPercentileValues<T>(Func<T> getValue, int size = 2)
        {
            var percentiles = new Dictionary<double, T>(size);

            for (int i = 0; i < size; ++i)
                percentiles.Add(RandomGenerator.GetRandomDouble(), getValue());

            return percentiles;
        }
    }
}
