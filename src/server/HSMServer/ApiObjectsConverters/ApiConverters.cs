using HSMSensorDataObjects.BarData;
using HSMSensorDataObjects.FullDataObject;
using HSMSensorDataObjects.HistoryRequests;
using HSMServer.Core.Model;
using HSMServer.Core.Model.HistoryValues;
using HSMServer.Core.Model.Requests;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ApiSensorStatus = HSMSensorDataObjects.SensorStatus;

namespace HSMServer.ApiObjectsConverters
{
    public static class ApiConverters
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



        public static SimpleSensorHistory Convert<T>(this BaseValue<T> value) =>
            new()
            {
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status.ToString(),
                Value = value.Value.ToString(),
            };

        public static FileSensorHistory Convert(this FileValue value) =>
            new()
            {
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status.ToString(),
                Value = value.Value,
                FileName = value.Name,
                Extension = value.Extension
            };

        public static BarSensorHistory Convert<T>(this BarBaseValue<T> value) where T : struct =>
            new()
            {
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status.ToString(),
                Count = value.Count,
                OpenTime = value.OpenTime,
                CloseTime = value.CloseTime,
                Min = value.Min.ToString(),
                Max = value.Max.ToString(),
                Mean = value.Mean.ToString(),
                LastValue = value.LastValue.ToString(),
                Percentiles = value.Percentiles?.Select(p => new PercentileValue { Percentile = p.Key, Value = p.Value.ToString() }).ToList() ?? new(),
            };

        public static object Convert(this BaseValue value) =>
            value switch
            {
                BooleanValue sv => sv.Convert(),
                IntegerValue sv => sv.Convert(),
                DoubleValue sv => sv.Convert(),
                StringValue sv => sv.Convert(),
                IntegerBarValue sv => sv.Convert(),
                DoubleBarValue sv => sv.Convert(),
                FileValue sv => sv.Convert(),
                _ => null,
            };

        public static List<object> Convert(this List<BaseValue> values)
        {
            var apiValues = new List<object>(values.Count);

            foreach (var value in values)
            {
                var apiValue = value.Convert();
                if (apiValue != null)
                    apiValues.Add(apiValue);
            }

            return apiValues;
        }


        public static HistoryRequestModel Convert(this HistoryRequest request) =>
            new(request.Key, request.Path)
            {
                From = request.From,
                To = request.To,
                Count = request.Count
            };


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
