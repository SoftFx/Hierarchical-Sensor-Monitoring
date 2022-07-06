using HSMSensorDataObjects.BarData;
using HSMSensorDataObjects.FullDataObject;
using HSMSensorDataObjects.TypedDataObject;
using HSMServer.Core.Model;
using System;
using System.Text.Json;

namespace HSMServer.Core.Converters
{
    public static class ApiConverter
    {
        public static BooleanValue Convert(this BoolSensorValue value) =>
            new()
            {
                Key = value.Key,
                Path = value.Path,
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status,
                Value = value.Value
            };

        public static BooleanValue ConvertToBool(this UnitedSensorValue value) =>
            new()
            {
                Key = value.Key,
                Path = value.Path,
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status,
                Value = bool.TryParse(value.Data, out var result) && result,
            };

        public static IntegerValue Convert(this IntSensorValue value) =>
            new()
            {
                Key = value.Key,
                Path = value.Path,
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status,
                Value = value.Value
            };

        public static IntegerValue ConvertToInt(this UnitedSensorValue value) =>
            new()
            {
                Key = value.Key,
                Path = value.Path,
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status,
                Value = int.TryParse(value.Data, out var result) ? result : 0
            };

        public static DoubleValue Convert(this DoubleSensorValue value) =>
            new()
            {
                Key = value.Key,
                Path = value.Path,
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status,
                Value = value.Value
            };

        public static DoubleValue ConvertToDouble(this UnitedSensorValue value) =>
            new()
            {
                Key = value.Key,
                Path = value.Path,
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status,
                Value = double.TryParse(value.Data, out double result) ? result : 0
            };

        public static StringValue Convert(this StringSensorValue value) =>
            new()
            {
                Key = value.Key,
                Path = value.Path,
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status,
                Value = value.Value
            };

        public static StringValue ConvertToString(this UnitedSensorValue value) =>
            new()
            {
                Key = value.Key,
                Path = value.Path,
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status,
                Value = value.Data
            };

        public static FileValue Convert(this FileSensorBytesValue value) =>
            new()
            {
                Key = value.Key,
                Path = value.Path,
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status,
                Value = value.Value,
                Name = value.FileName,
                Extension = value.Extension,
                OriginalSize = value.Value.LongLength
            };

        public static IntegerBarValue Convert(this IntBarSensorValue value) =>
            new()
            {
                Key = value.Key,
                Path = value.Path,
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status,
                Count = value.Count,
                OpenTime = value.OpenTime,
                CloseTime = value.CloseTime,
                Min = value.Min,
                Max = value.Max,
                Mean = value.Mean,
                LastValue = value.LastValue
            };

        public static IntegerBarValue ConvertToIntBar(this UnitedSensorValue value)
        {
            var barData = JsonSerializer.Deserialize<IntBarData>(value.Data);

            return new()
            {
                Key = value.Key,
                Path = value.Path,
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status,
                Count = barData.Count,
                OpenTime = barData.StartTime.ToUniversalTime(),
                CloseTime = (barData.EndTime == DateTime.MinValue ? DateTime.Now : barData.EndTime).ToUniversalTime(),
                Min = barData.Min,
                Max = barData.Max,
                Mean = barData.Mean,
                LastValue = barData.LastValue
            };
        }

        public static DoubleBarValue Convert(this DoubleBarSensorValue value) =>
            new()
            {
                Key = value.Key,
                Path = value.Path,
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status,
                Count = value.Count,
                OpenTime = value.OpenTime,
                CloseTime = value.CloseTime,
                Min = value.Min,
                Max = value.Max,
                Mean = value.Mean,
                LastValue = value.LastValue
            };

        public static DoubleBarValue ConvertToDoubleBar(this UnitedSensorValue value)
        {
            var barData = JsonSerializer.Deserialize<DoubleBarData>(value.Data);

            return new()
            {
                Key = value.Key,
                Path = value.Path,
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status,
                Count = barData.Count,
                OpenTime = barData.StartTime,
                CloseTime = barData.EndTime,
                Min = barData.Min,
                Max = barData.Max,
                Mean = barData.Mean,
                LastValue = barData.LastValue
            };
        }
    }
}
