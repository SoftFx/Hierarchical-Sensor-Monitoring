using HSMServer.Core.Model.Policies;
using HSMServer.Model.DataAlerts;
using System;

namespace HSMServer.Extensions
{
    public static class AlertExtensions
    {
        public static string ToVisibility(this bool isVisible) => isVisible ? "d-flex" : "d-none";


        public static OperationViewModel GetOperations(this ConditionViewModel condition)
        {
            var viewModel = condition.GetOperations(condition.Property);

            viewModel.SetData(condition.Operation, condition.Target);

            return viewModel;
        }

        public static OperationViewModel GetOperations(this ConditionViewModel condition, PolicyProperty property) =>
            property switch
            {
                PolicyProperty.Status => new StatusOperation(),
                PolicyProperty.Comment => new CommentOperation(),

                PolicyProperty.Value when condition is StringConditionViewModel => new StringOperation(),

                PolicyProperty.Value or PolicyProperty.Min or PolicyProperty.Max or PolicyProperty.Mean or PolicyProperty.Count or
                PolicyProperty.LastValue or PolicyProperty.Length or PolicyProperty.OriginalSize => new NumericOperation(),

                _ => throw new NotSupportedException(),
            };

        public static IntervalOperationViewModel GetIntervalOperations(this ConditionViewModel condition) =>
            condition.GetIntervalOperations(condition.Property);

        public static IntervalOperationViewModel GetIntervalOperations(this ConditionViewModel condition, PolicyProperty property) =>
            property switch
            {
                PolicyProperty.Sensitivity => new SensitivityOperation(condition.Sensitivity),
                PolicyProperty.TimeToLive => new TimeToLiveOperation(condition.TimeToLive),
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
