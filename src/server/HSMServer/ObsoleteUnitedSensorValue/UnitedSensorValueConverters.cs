using HSMServer.Core.Model;
using System;
using System.Linq;
using System.Text.Json;
using ApiSensorStatus = HSMSensorDataObjects.SensorStatus;

namespace HSMServer.ObsoleteUnitedSensorValue
{
    [Obsolete("Remove this after removing supporting of DataCollector v2")]
    internal static class UnitedSensorValueConverters
    {
        public static BooleanValue ConvertToBool(this UnitedSensorValue value) =>
            new()
            {
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status.Convert(),
                Value = bool.TryParse(value.Data, out var result) && result,
            };

        public static IntegerValue ConvertToInt(this UnitedSensorValue value) =>
            new()
            {
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status.Convert(),
                Value = int.TryParse(value.Data, out var result) ? result : 0
            };

        public static DoubleValue ConvertToDouble(this UnitedSensorValue value) =>
            new()
            {
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status.Convert(),
                Value = double.TryParse(value.Data, out double result) ? result : 0
            };

        public static StringValue ConvertToString(this UnitedSensorValue value) =>
            new()
            {
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status.Convert(),
                Value = value.Data
            };

        public static IntegerBarValue ConvertToIntBar(this UnitedSensorValue value)
        {
            var barData = JsonSerializer.Deserialize<IntBarData>(value.Data);

            return new()
            {
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status.Convert(),
                Count = barData.Count,
                OpenTime = barData.StartTime.ToUniversalTime(),
                CloseTime = barData.EndTime.ToUniversalTime(),
                Min = barData.Min,
                Max = barData.Max,
                Mean = barData.Mean,
                LastValue = barData.LastValue,
                Percentiles = barData.Percentiles?.ToDictionary(k => k.Percentile, v => v.Value) ?? new(),
            };
        }

        public static DoubleBarValue ConvertToDoubleBar(this UnitedSensorValue value)
        {
            var barData = JsonSerializer.Deserialize<DoubleBarData>(value.Data);

            return new()
            {
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status.Convert(),
                Count = barData.Count,
                OpenTime = barData.StartTime.ToUniversalTime(),
                CloseTime = barData.EndTime.ToUniversalTime(),
                Min = barData.Min,
                Max = barData.Max,
                Mean = barData.Mean,
                LastValue = barData.LastValue,
                Percentiles = barData.Percentiles?.ToDictionary(k => k.Percentile, v => v.Value) ?? new(),
            };
        }

        private static SensorStatus Convert(this ApiSensorStatus status) =>
            status switch
            {
                ApiSensorStatus.Ok => SensorStatus.Ok,
                ApiSensorStatus.OffTime => SensorStatus.OffTime,
                ApiSensorStatus.Error => SensorStatus.Error,
                ApiSensorStatus.Warning => SensorStatus.Warning,
                _ => SensorStatus.Ok
            };
    }
}
