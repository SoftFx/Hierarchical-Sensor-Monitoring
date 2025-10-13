using System;
using System.Collections.Generic;
using System.Numerics;
using HSMSensorDataObjects.SensorRequests;
using HSMSensorDataObjects.SensorValueRequests;
using HSMServer.Core.Extensions;
using HSMServer.Core.Model;
using HSMServer.Core.Model.HistoryValues;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Services;
using ApiSensorStatus = HSMSensorDataObjects.SensorStatus;
using HSMDataCollector.DefaultSensors;


namespace HSMServer.Core.ApiObjectsConverters
{
    public static class ApiConverters
    {
        public static BooleanValue Convert(this BoolSensorValue value) =>
            new()
            {
                Comment = HtmlSanitizerService.Sanitize(value.Comment),
                Time = value.Time,
                Status = value.Status.Convert(),
                Value = value.Value,
            };


        public static IntegerValue Convert(this IntSensorValue value) =>
            new()
            {
                Comment = HtmlSanitizerService.Sanitize(value.Comment),
                Time = value.Time,
                Status = value.Status.Convert(),
                Value = value.Value
            };


        public static DoubleValue Convert(this DoubleSensorValue value) =>
            new()
            {
                Comment = HtmlSanitizerService.Sanitize(value.Comment),
                Time = value.Time,
                Status = value.Status.Convert(),
                Value = value.Value
            };


        public static StringValue Convert(this StringSensorValue value) =>
            new()
            {
                Comment = HtmlSanitizerService.Sanitize(value.Comment),
                Time = value.Time,
                Status = value.Status.Convert(),
                Value = HtmlSanitizerService.Sanitize(value.Value)
            };


        public static TimeSpanValue Convert(this TimeSpanSensorValue value) =>
            new()
            {
                Comment = HtmlSanitizerService.Sanitize(value.Comment),
                Time = value.Time,
                Status = value.Status.Convert(),
                Value = value.Value
            };


        public static VersionValue Convert(this VersionSensorValue value) =>

            new()
            {
                Comment = HtmlSanitizerService.Sanitize(value.Comment),
                Time = value.Time,
                Status = value.Status.Convert(),
                Value = value.Value
            };


        public static RateValue Convert(this RateSensorValue value) =>
            new()
            {
                Comment = HtmlSanitizerService.Sanitize(value.Comment),
                Time = value.Time,
                Status = value.Status.Convert(),
                Value = value.Value
            };


        public static FileValue Convert(this FileSensorValue value) =>
            new()
            {
                Comment = HtmlSanitizerService.Sanitize(value.Comment),
                Time = value.Time,
                Status = value.Status.Convert(),
                Value = value.Value?.ToArray() ?? [],
                Name = value.Name,
                Extension = value.Extension,
                OriginalSize = value.Value?.Count ?? 0L
            };

        public static IntegerBarValue Convert(this IntBarSensorValue value) =>
            new()
            {
                Comment = HtmlSanitizerService.Sanitize(value.Comment),
                Time = value.Time,
                Status = value.Status.Convert(),
                Count = value.Count,
                OpenTime = value.OpenTime,
                CloseTime = value.CloseTime,
                Min = value.Min,
                Max = value.Max,
                Mean = value.Mean,
                FirstValue = value.FirstValue,
                LastValue = value.LastValue,
            };


        public static DoubleBarValue Convert(this DoubleBarSensorValue value) =>
            new()
            {
                Comment = HtmlSanitizerService.Sanitize(value.Comment),
                Time = value.Time,
                Status = value.Status.Convert(),
                Count = value.Count,
                OpenTime = value.OpenTime,
                CloseTime = value.CloseTime,
                Min = value.Min,
                Max = value.Max,
                Mean = value.Mean,
                FirstValue = value.FirstValue,
                LastValue = value.LastValue,
            };

        public static EnumValue Convert(this EnumSensorValue value) =>
            new()
            {
                Comment = HtmlSanitizerService.Sanitize(value.Comment),
                Time = value.Time,
                Status = value.Status.Convert(),
                Value = value.Value
            };

        public static IntegerBarValue Convert(this IntMonitoringBar value) =>
            new()
            {
                Comment = HtmlSanitizerService.Sanitize(value.Comment),
                Time = value.Time,
                Status = value.Status.Convert(),
                Count = value.Count,
                OpenTime = value.OpenTime,
                CloseTime = value.CloseTime,
                Min = value.Min,
                Max = value.Max,
                Mean = value.Mean,
                FirstValue = value.FirstValue,
                LastValue = value.LastValue,
            };


        public static DoubleBarValue Convert(this DoubleMonitoringBar value) =>
            new()
            {
                Comment = HtmlSanitizerService.Sanitize(value.Comment),
                Time = value.Time,
                Status = value.Status.Convert(),
                Count = value.Count,
                OpenTime = value.OpenTime,
                CloseTime = value.CloseTime,
                Min = value.Min,
                Max = value.Max,
                Mean = value.Mean,
                FirstValue = value.FirstValue,
                LastValue = value.LastValue,
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
                RateSensorValue sv => sv.Convert(),
                EnumSensorValue sv => sv.Convert(),
                IntMonitoringBar sv => sv.Convert(),
                DoubleMonitoringBar sv => sv.Convert(),
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


        public static BarSensorHistory Convert<T>(this BarBaseValue<T> value) where T : struct, INumber<T> =>
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
                FirstValue = value.FirstValue?.ToString(),
                LastValue = value.LastValue.ToString(),
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
                RateValue sv => sv.Convert(),
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




  


        public static SensorStatus Convert(this ApiSensorStatus status) =>
            status switch
            {
                ApiSensorStatus.Ok => SensorStatus.Ok,
                ApiSensorStatus.OffTime => SensorStatus.OffTime,
                ApiSensorStatus.Error or ApiSensorStatus.Warning => SensorStatus.Error,
                _ => SensorStatus.Ok
            };

    }
}