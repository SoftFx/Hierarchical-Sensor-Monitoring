using HSMCommon.Extensions;
using HSMServer.Core.Model.Policies;
using HSMServer.Model.DataAlerts;
using System;

namespace HSMServer.Extensions
{
    public static class AlertExtensions
    {
        public static string ToVisibility(this bool isVisible) => isVisible ? "d-flex" : "d-none";


        public static AlertProperty ToClient(this PolicyProperty property) =>
            property switch
            {
                PolicyProperty.Status => AlertProperty.Status,
                PolicyProperty.Comment => AlertProperty.Comment,
                PolicyProperty.Value => AlertProperty.Value,
                PolicyProperty.EmaValue => AlertProperty.EmaValue,
                PolicyProperty.Min => AlertProperty.Min,
                PolicyProperty.Max => AlertProperty.Max,
                PolicyProperty.Mean => AlertProperty.Mean,
                PolicyProperty.Count => AlertProperty.Count,
                PolicyProperty.FirstValue => AlertProperty.FirstValue,
                PolicyProperty.LastValue => AlertProperty.LastValue,
                PolicyProperty.EmaMin => AlertProperty.EmaMin,
                PolicyProperty.EmaMax => AlertProperty.EmaMax,
                PolicyProperty.EmaMean => AlertProperty.EmaMean,
                PolicyProperty.EmaCount => AlertProperty.EmaCount,
                PolicyProperty.Length => AlertProperty.Length,
                PolicyProperty.OriginalSize => AlertProperty.OriginalSize,
                PolicyProperty.NewSensorData => AlertProperty.NewSensorData,
                _ => throw new NotImplementedException()
            };

        public static PolicyProperty ToCore(this AlertProperty property) =>
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
                _ => throw new NotImplementedException()
            };


        public static ScheduleRepeatMode? ToClient(this AlertRepeatMode repeatMode) =>
            repeatMode switch
            {
                AlertRepeatMode.Hourly => ScheduleRepeatMode.Hourly,
                AlertRepeatMode.Daily => ScheduleRepeatMode.Daily,
                AlertRepeatMode.Weekly => ScheduleRepeatMode.Weekly,
                _ => null,
            };

        public static AlertRepeatMode ToCore(this ScheduleRepeatMode? repeatMode) =>
            repeatMode switch
            {
                ScheduleRepeatMode.Hourly => AlertRepeatMode.Hourly,
                ScheduleRepeatMode.Daily => AlertRepeatMode.Daily,
                ScheduleRepeatMode.Weekly => AlertRepeatMode.Weekly,
                _ => AlertRepeatMode.Immediately,
            };


        public static DateTime? ToClientScheduleTime(this DateTime time) => time == DateTime.MinValue ? DateTime.UtcNow.Ceil(TimeSpan.FromHours(1)) : time;

        public static DateTime ToCoreScheduleTime(this DateTime? time) => time ?? DateTime.MinValue;


        public static OperationViewModel GetOperations(this ConditionViewModel condition)
        {
            var viewModel = condition.GetOperations(condition.Property);

            return viewModel.SetData(condition.Operation, condition.Target);
        }

        public static OperationViewModel GetOperations(this ConditionViewModel condition, AlertProperty property) =>
            property switch
            {
                AlertProperty.Status => new StatusOperation(),
                AlertProperty.Comment => new CommentOperation(),

                AlertProperty.Value when condition is StringConditionViewModel => new StringOperation(),

                AlertProperty.Value or AlertProperty.EmaValue or
                AlertProperty.Min or AlertProperty.Max or AlertProperty.Mean or AlertProperty.Count or AlertProperty.FirstValue or AlertProperty.LastValue or
                AlertProperty.EmaMin or AlertProperty.EmaMax or AlertProperty.EmaMean or AlertProperty.EmaCount or
                AlertProperty.Length or AlertProperty.OriginalSize => new NumericOperation(),

                _ => throw new NotSupportedException(),
            };

        public static IntervalOperationViewModel GetIntervalOperations(this ConditionViewModel condition) =>
            condition.GetIntervalOperations(condition.Property);

        public static IntervalOperationViewModel GetIntervalOperations(this ConditionViewModel condition, AlertProperty property) =>
            property switch
            {
                AlertProperty.ConfirmationPeriod => new ConfirmationPeriodOperation(condition.ConfirmationPeriod),
                AlertProperty.TimeToLive => new TimeToLiveOperation(condition.TimeToLive),
                _ => throw new NotSupportedException(),
            };


        public static bool IsTargetVisible(this PolicyOperation operation) =>
            operation switch
            {
                PolicyOperation.IsChanged or PolicyOperation.IsError or PolicyOperation.IsOk or
                PolicyOperation.IsChangedToError or PolicyOperation.IsChangedToOk or PolicyOperation.ReceivedNewValue => false,

                _ => true,
            };
    }
}
