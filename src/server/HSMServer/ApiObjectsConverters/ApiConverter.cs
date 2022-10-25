using HSMSensorDataObjects.BarData;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Model;
using System.Linq;
using System.Text.Json;
using ObjectsSensorStatus = HSMSensorDataObjects.SensorStatus;

namespace HSMServer.ApiObjectsConverters
{
    public static class ApiConverter
    {
        public static BooleanValue Convert(this BoolSensorValue value) =>
            new()
            {
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status.Convert(),
                Value = value.Value
            };

        public static BooleanValue ConvertToBool(this UnitedSensorValue value) =>
            new()
            {
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status.Convert(),
                Value = bool.TryParse(value.Data, out var result) && result,
            };

        public static IntegerValue Convert(this IntSensorValue value) =>
            new()
            {
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status.Convert(),
                Value = value.Value
            };

        public static IntegerValue ConvertToInt(this UnitedSensorValue value) =>
            new()
            {
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status.Convert(),
                Value = int.TryParse(value.Data, out var result) ? result : 0
            };

        public static DoubleValue Convert(this DoubleSensorValue value) =>
            new()
            {
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status.Convert(),
                Value = value.Value
            };

        public static DoubleValue ConvertToDouble(this UnitedSensorValue value) =>
            new()
            {
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status.Convert(),
                Value = double.TryParse(value.Data, out double result) ? result : 0
            };

        public static StringValue Convert(this StringSensorValue value) =>
            new()
            {
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status.Convert(),
                Value = value.Value
            };

        public static StringValue ConvertToString(this UnitedSensorValue value) =>
            new()
            {
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status.Convert(),
                Value = value.Data
            };

        public static FileValue Convert(this FileSensorBytesValue value) =>
            new()
            {
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status.Convert(),
                Value = value.Value,
                Name = value.FileName,
                Extension = value.Extension,
                OriginalSize = value.Value.LongLength
            };

        public static IntegerBarValue Convert(this IntBarSensorValue value) =>
            new()
            {
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status.Convert(),
                Count = value.Count,
                OpenTime = value.OpenTime,
                CloseTime = value.CloseTime,
                Min = value.Min,
                Max = value.Max,
                Mean = value.Mean,
                LastValue = value.LastValue,
                Percentiles = value.Percentiles?.ToDictionary(k => k.Percentile, v => v.Value) ?? new(),
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

        public static DoubleBarValue Convert(this DoubleBarSensorValue value) =>
            new()
            {
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status.Convert(),
                Count = value.Count,
                OpenTime = value.OpenTime,
                CloseTime = value.CloseTime,
                Min = value.Min,
                Max = value.Max,
                Mean = value.Mean,
                LastValue = value.LastValue,
                Percentiles = value.Percentiles?.ToDictionary(k => k.Percentile, v => v.Value) ?? new(),
            };

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

        public static SensorStatus Convert(this ObjectsSensorStatus status) =>
            status switch
            {
                ObjectsSensorStatus.Ok => SensorStatus.Ok,
                ObjectsSensorStatus.Unknown => SensorStatus.Unknown,
                ObjectsSensorStatus.Error => SensorStatus.Error,
                ObjectsSensorStatus.Warning => SensorStatus.Warning,
                _ => SensorStatus.Unknown
            };

        public static BaseValue Convert(this SensorValueBase value) =>
            value switch
            {
                IntBarSensorValue sv => sv.Convert(),
                DoubleBarSensorValue sv => sv.Convert(),
                DoubleSensorValue sv => sv.Convert(),
                IntSensorValue sv => sv.Convert(),
                BoolSensorValue sv => sv.Convert(),
                StringSensorValue sv => sv.Convert(),
                _ => null
            };
    }
}
