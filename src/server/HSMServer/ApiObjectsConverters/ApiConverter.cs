using HSMSensorDataObjects.BarData;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Model;
using System.Collections.Generic;
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



        public static BoolSensorValue Convert(this BooleanValue value, string key, string path) =>
            new()
            {
                Key = key,
                Path = path,
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status.Convert(),
                Value = value.Value
            };

        public static IntSensorValue Convert(this IntegerValue value, string key, string path) =>
            new()
            {
                Key = key,
                Path = path,
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status.Convert(),
                Value = value.Value
            };

        public static DoubleSensorValue Convert(this DoubleValue value, string key, string path) =>
            new()
            {
                Key = key,
                Path = path,
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status.Convert(),
                Value = value.Value
            };

        public static StringSensorValue Convert(this StringValue value, string key, string path) =>
            new()
            {
                Key = key,
                Path = path,
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status.Convert(),
                Value = value.Value
            };

        public static FileSensorBytesValue Convert(this FileValue value, string key, string path) =>
            new()
            {
                Key = key,
                Path = path,
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status.Convert(),
                Value = value.Value,
                FileName = value.Name,
                Extension = value.Extension
            };

        public static IntBarSensorValue Convert(this IntegerBarValue value, string key, string path) =>
            new()
            {
                Key = key,
                Path = path,
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
                Percentiles = value.Percentiles?.Select(p => new PercentileValueInt { Percentile = p.Key, Value = p.Value }).ToList() ?? new(),
            };

        public static DoubleBarSensorValue Convert(this DoubleBarValue value, string key, string path) =>
            new()
            {
                Key = key,
                Path = path,
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
                Percentiles = value.Percentiles?.Select(p => new PercentileValueDouble { Percentile = p.Key, Value = p.Value }).ToList() ?? new(),
            };

        public static object Convert(this BaseValue value, string key, string path) =>
            value switch
            {
                BooleanValue sv => sv.Convert(key, path),
                IntegerValue sv => sv.Convert(key, path),
                DoubleValue sv => sv.Convert(key, path),
                StringValue sv => sv.Convert(key, path),
                IntegerBarValue sv => sv.Convert(key, path),
                DoubleBarValue sv => sv.Convert(key, path),
                FileValue sv => sv.Convert(key, path),
                _ => null,
            };

        public static List<object> Convert(this List<BaseValue> values, string key, string path)
        {
            var apiValues = new List<object>(values.Count);

            foreach (var value in values)
            {
                var apiValue = value.Convert(key, path);
                if (apiValue != null)
                    apiValues.Add(apiValue);
            }

            return apiValues;
        }


        private static SensorStatus Convert(this ObjectsSensorStatus status) =>
            status switch
            {
                ObjectsSensorStatus.Ok => SensorStatus.Ok,
                ObjectsSensorStatus.Unknown => SensorStatus.Unknown,
                ObjectsSensorStatus.Error => SensorStatus.Error,
                ObjectsSensorStatus.Warning => SensorStatus.Warning,
                _ => SensorStatus.Unknown
            };

        private static ObjectsSensorStatus Convert(this SensorStatus status) =>
            status switch
            {
                SensorStatus.Ok => ObjectsSensorStatus.Ok,
                SensorStatus.Unknown => ObjectsSensorStatus.Unknown,
                SensorStatus.Error => ObjectsSensorStatus.Error,
                SensorStatus.Warning => ObjectsSensorStatus.Warning,
                _ => ObjectsSensorStatus.Unknown
            };
    }
}
