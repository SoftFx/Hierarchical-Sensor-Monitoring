using HSMSensorDataObjects.HistoryRequests;
using HSMSensorDataObjects.SensorRequests;
using HSMSensorDataObjects.SensorValueRequests;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Extensions;
using HSMServer.Core.Model;
using HSMServer.Core.Model.HistoryValues;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.TableOfChanges;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
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

        public static CounterValue Convert(this CounterSensorValue value)
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
                Value = value.Value?.ToArray() ?? Array.Empty<byte>(),
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
                FirstValue = value.FirstValue,
                LastValue = value.LastValue,
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
                CounterSensorValue sv => sv.Convert(),
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
                CounterValue sv => sv.Convert(),
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
                Count = request.Count,
                Options = request.Options
            };


        public static SensorUpdate Convert(this AddOrUpdateSensorRequest request, Guid sensorId, string keyName)
        {
            var initiator = InitiatorInfo.AsCollector(keyName, request.IsForceUpdate);

            return new()
            {
                Id = sensorId,
                Description = request.Description,
                IsSingleton = request.IsSingletonSensor,
                AggregateValues = request.AggregateData,
                Statistics = request.Statistics?.Convert(),
                SelectedUnit = request.OriginalUnit?.Convert(),
                Integration = request.EnableGrafana.HasValue ? request.EnableGrafana.Value ? Integration.Grafana : Integration.None : null,
                KeepHistory = request.KeepHistory.ToTimeInterval(),
                SelfDestroy = request.SelfDestroy.ToTimeInterval(),
                TTL = request.TTL.ToTimeInterval(),
                TTLPolicy = request.TtlAlert?.Convert(initiator),
                Policies = request.Alerts?.Select(policy => policy.Convert(initiator)).ToList(),
                DefaultAlertsOptions = (Core.Model.DefaultAlertsOptions)request.DefaultAlertsOptions,
                Initiator = initiator,
            };
        }


        public static PolicyUpdate Convert(this AlertUpdateRequest request, InitiatorInfo initiator) => new()
        {
            Conditions = request.Conditions?.Select(c => c.Convert()).ToList(),
            Destination = new(),

            Schedule = new PolicyScheduleUpdate(request.ScheduledNotificationTime ?? DateTime.MinValue,
                                                request.ScheduledRepeatMode.HasValue ? request.ScheduledRepeatMode.Value.Convert() : Core.Model.Policies.AlertRepeatMode.Immediately),

            Id = Guid.Empty,
            Status = request.Status.Convert(),
            Template = request.Template,
            Icon = request.Icon,
            IsDisabled = request.IsDisabled,
            ConfirmationPeriod = request.ConfirmationPeriod,
            Initiator = initiator,
        };


        public static PolicyConditionUpdate Convert(this AlertConditionUpdate request) =>
            new(request.Operation.Convert(),
                request.Property.Convert(),
                request.Target is not null ? new(request.Target.Type.Convert(), request.Target.Value) : null,
                request.Combination.Convert());


        private static TimeIntervalModel ToTimeInterval(this long? ticks)
        {
            return !ticks.HasValue ? null : ticks.Value == TimeSpan.MaxValue.Ticks ? new TimeIntervalModel(TimeInterval.None) : new(ticks.Value);
        }


        public static SensorType Convert(this HSMSensorDataObjects.SensorType type) =>
            type switch
            {
                HSMSensorDataObjects.SensorType.BooleanSensor => SensorType.Boolean,
                HSMSensorDataObjects.SensorType.IntSensor => SensorType.Integer,
                HSMSensorDataObjects.SensorType.DoubleSensor => SensorType.Double,
                HSMSensorDataObjects.SensorType.StringSensor => SensorType.String,
                HSMSensorDataObjects.SensorType.TimeSpanSensor => SensorType.TimeSpan,
                HSMSensorDataObjects.SensorType.VersionSensor => SensorType.Version,
                HSMSensorDataObjects.SensorType.FileSensor => SensorType.File,
                HSMSensorDataObjects.SensorType.IntegerBarSensor => SensorType.IntegerBar,
                HSMSensorDataObjects.SensorType.DoubleBarSensor => SensorType.DoubleBar,
                HSMSensorDataObjects.SensorType.CounterSensor => SensorType.Counter,
                _ => throw new NotImplementedException(),
            };


        private static SensorStatus Convert(this ApiSensorStatus status) =>
            status switch
            {
                ApiSensorStatus.Ok => SensorStatus.Ok,
                ApiSensorStatus.OffTime => SensorStatus.OffTime,
                ApiSensorStatus.Error or ApiSensorStatus.Warning => SensorStatus.Error,
                _ => SensorStatus.Ok
            };


        private static PolicyProperty Convert(this AlertProperty property) =>
            property switch
            {
                AlertProperty.Status => PolicyProperty.Status,
                AlertProperty.Comment => PolicyProperty.Comment,
                AlertProperty.Value => PolicyProperty.Value,
                AlertProperty.EmaValue => PolicyProperty.EmaValue,
                AlertProperty.Min => PolicyProperty.Min,
                AlertProperty.Max => PolicyProperty.Max,
                AlertProperty.Mean => PolicyProperty.Mean,
                AlertProperty.Count => PolicyProperty.Count,
                AlertProperty.FirstValue => PolicyProperty.FirstValue,
                AlertProperty.LastValue => PolicyProperty.LastValue,
                AlertProperty.EmaMin => PolicyProperty.EmaMin,
                AlertProperty.EmaMax => PolicyProperty.EmaMax,
                AlertProperty.EmaMean => PolicyProperty.EmaMean,
                AlertProperty.EmaCount => PolicyProperty.EmaCount,
                AlertProperty.Length => PolicyProperty.Length,
                AlertProperty.OriginalSize => PolicyProperty.OriginalSize,
                AlertProperty.NewSensorData => PolicyProperty.NewSensorData,
                _ => throw new NotImplementedException(),
            };


        private static PolicyOperation Convert(this AlertOperation operation) =>
            operation switch
            {
                AlertOperation.LessThanOrEqual => PolicyOperation.LessThanOrEqual,
                AlertOperation.LessThan => PolicyOperation.LessThan,
                AlertOperation.GreaterThan => PolicyOperation.GreaterThan,
                AlertOperation.GreaterThanOrEqual => PolicyOperation.GreaterThanOrEqual,
                AlertOperation.Equal => PolicyOperation.Equal,
                AlertOperation.NotEqual => PolicyOperation.NotEqual,
                AlertOperation.IsChanged => PolicyOperation.IsChanged,
                AlertOperation.IsError => PolicyOperation.IsError,
                AlertOperation.IsOk => PolicyOperation.IsOk,
                AlertOperation.IsChangedToError => PolicyOperation.IsChangedToError,
                AlertOperation.IsChangedToOk => PolicyOperation.IsChangedToOk,
                AlertOperation.Contains => PolicyOperation.Contains,
                AlertOperation.StartsWith => PolicyOperation.StartsWith,
                AlertOperation.EndsWith => PolicyOperation.EndsWith,
                AlertOperation.ReceivedNewValue => PolicyOperation.ReceivedNewValue,
                _ => throw new NotImplementedException(),
            };


        private static Core.Model.Policies.TargetType Convert(this HSMSensorDataObjects.SensorRequests.TargetType target) =>
            target switch
            {
                HSMSensorDataObjects.SensorRequests.TargetType.Const => Core.Model.Policies.TargetType.Const,
                HSMSensorDataObjects.SensorRequests.TargetType.LastValue => Core.Model.Policies.TargetType.LastValue,
                _ => throw new NotImplementedException(),
            };


        private static PolicyCombination Convert(this AlertCombination combination) =>
            combination switch
            {
                AlertCombination.And => PolicyCombination.And,
                AlertCombination.Or => PolicyCombination.Or,
                _ => throw new NotImplementedException(),
            };


        private static Core.Model.Unit Convert(this HSMSensorDataObjects.SensorRequests.Unit unit) =>
            unit switch
            {
                HSMSensorDataObjects.SensorRequests.Unit.bits => Core.Model.Unit.bits,
                HSMSensorDataObjects.SensorRequests.Unit.bytes => Core.Model.Unit.bytes,
                HSMSensorDataObjects.SensorRequests.Unit.KB => Core.Model.Unit.KB,
                HSMSensorDataObjects.SensorRequests.Unit.MB => Core.Model.Unit.MB,
                HSMSensorDataObjects.SensorRequests.Unit.GB => Core.Model.Unit.GB,

                HSMSensorDataObjects.SensorRequests.Unit.Percents => Core.Model.Unit.Percents,

                HSMSensorDataObjects.SensorRequests.Unit.Ticks => Core.Model.Unit.Ticks,
                HSMSensorDataObjects.SensorRequests.Unit.Milliseconds => Core.Model.Unit.Milliseconds,
                HSMSensorDataObjects.SensorRequests.Unit.Seconds => Core.Model.Unit.Seconds,
                HSMSensorDataObjects.SensorRequests.Unit.Minutes => Core.Model.Unit.Minutes,

                HSMSensorDataObjects.SensorRequests.Unit.Count => Core.Model.Unit.Count,
                HSMSensorDataObjects.SensorRequests.Unit.Requests => Core.Model.Unit.Requests,
                HSMSensorDataObjects.SensorRequests.Unit.Responses => Core.Model.Unit.Responses,

                HSMSensorDataObjects.SensorRequests.Unit.Bits_sec => Core.Model.Unit.Bits_sec,
                HSMSensorDataObjects.SensorRequests.Unit.Bytes_sec => Core.Model.Unit.Bytes_sec,
                HSMSensorDataObjects.SensorRequests.Unit.KBytes_sec => Core.Model.Unit.KBytes_sec,
                HSMSensorDataObjects.SensorRequests.Unit.MBytes_sec => Core.Model.Unit.MBytes_sec,

                _ => throw new NotImplementedException(),
            };


        private static Core.Model.StatisticsOptions Convert(this HSMSensorDataObjects.SensorRequests.StatisticsOptions combination) =>
            combination switch
            {
                HSMSensorDataObjects.SensorRequests.StatisticsOptions.None => Core.Model.StatisticsOptions.None,
                HSMSensorDataObjects.SensorRequests.StatisticsOptions.EMA => Core.Model.StatisticsOptions.EMA,
                _ => throw new NotImplementedException(),
            };


        private static Core.Model.Policies.AlertRepeatMode Convert(this HSMSensorDataObjects.SensorRequests.AlertRepeatMode repeatMode) =>
            repeatMode switch
            {
                HSMSensorDataObjects.SensorRequests.AlertRepeatMode.Hourly => Core.Model.Policies.AlertRepeatMode.Hourly,
                HSMSensorDataObjects.SensorRequests.AlertRepeatMode.Daily => Core.Model.Policies.AlertRepeatMode.Daily,
                HSMSensorDataObjects.SensorRequests.AlertRepeatMode.Weekly => Core.Model.Policies.AlertRepeatMode.Weekly,
                _ => throw new NotImplementedException(),
            };
    }
}