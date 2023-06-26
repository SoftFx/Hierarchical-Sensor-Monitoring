using HSMSensorDataObjects.HistoryRequests;
using HSMSensorDataObjects.SensorValueRequests;
using HSMServer.Core.Helpers;
using HSMServer.Core.Model;
using HSMServer.Core.Model.HistoryValues;
using HSMServer.Core.Model.Requests;
using System;
using System.Collections.Generic;
using System.Linq;
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

        public static IntegerValue Convert(this IntSensorValue value) =>
            new()
            {
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status.Convert(),
                Value = value.Value
            };

        public static DoubleValue Convert(this DoubleSensorValue value) =>
            new()
            {
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status.Convert(),
                Value = value.Value
            };

        public static StringValue Convert(this StringSensorValue value) =>
            new()
            {
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status.Convert(),
                Value = value.Value
            };

        public static TimeSpanValue Convert(this TimeSpanSensorValue value) =>
            new()
            {
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status.Convert(),
                Value = value.Value
            };

        public static VersionValue Convert(this VersionSensorValue value)
        {
            return new()
            {
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status.Convert(),
                Value = value.Value
            };
        }

        public static FileValue Convert(this FileSensorValue value) =>
            new()
            {
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status.Convert(),
                Value = value.Value is null ? Array.Empty<byte>() : value.Value.ToArray(),
                Name = value.Name,
                Extension = value.Extension,
                OriginalSize = value.Value?.Count ?? 0L
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
                Percentiles = value.Percentiles ?? new(),
            };

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
                Percentiles = value.Percentiles ?? new(),
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
                TimeSpanSensorValue sv => sv.Convert(),
                VersionSensorValue sv => sv.Convert(),
                FileSensorValue sv => sv.Convert(),
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

        public static FileSensorHistory Convert(this FileValue fileValue)
        {
            var value = fileValue.DecompressContent(); // TODO smth with this crutch

            return new()
            {
                Comment = value.Comment,
                Time = value.Time,
                Status = value.Status.ToString(),
                Value = value.Value,
                FileName = value.Name,
                Extension = value.Extension
            };
        }

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


        public static HistoryRequestModel Convert(this HistoryRequest request, string key) =>
            new(string.IsNullOrEmpty(key) ? request.Key : key, request.Path)
            {
                From = request.From,
                To = request.To,
                Count = request.Count
            };

        public static SensorValueBase CreateNewSensorValue(SensorType sensorType) => sensorType switch
        {
            SensorType.Boolean => new BoolSensorValue(),
            SensorType.IntegerBar => new IntBarSensorValue(),
            SensorType.DoubleBar => new DoubleBarSensorValue(),
            SensorType.Double => new DoubleSensorValue(),
            SensorType.Integer => new IntSensorValue(),
            SensorType.String => new StringSensorValue(),
            SensorType.File => new FileSensorValue(),
            SensorType.TimeSpan => new TimeSpanSensorValue(),
            SensorType.Version => new VersionSensorValue(),
            _ => null
        };

        public static ApiSensorStatus ToApi(this Model.TreeViewModel.SensorStatus status) =>
            status switch
            {
                Model.TreeViewModel.SensorStatus.Ok => ApiSensorStatus.Ok,
                Model.TreeViewModel.SensorStatus.Warning => ApiSensorStatus.Warning,
                Model.TreeViewModel.SensorStatus.Error => ApiSensorStatus.Error,
                Model.TreeViewModel.SensorStatus.OffTime => ApiSensorStatus.OffTime,
                _ => ApiSensorStatus.Ok,
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
