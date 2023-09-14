using System;

namespace HSMServer.Model.DataAlerts
{
    public static class PropertyOperations
    {
        public static OperationViewModel GetOperations(this ConditionViewModel condition)
        {
            var viewModel = condition.GetOperations(condition.Property);

            viewModel?.SetData(condition.Operation, condition.Target);

            return viewModel;
        }

        public static OperationViewModel GetOperations(this ConditionViewModel condition, AlertProperty property) =>
            property switch
            {
                AlertProperty.Status => new StatusOperation(),
                AlertProperty.Comment => new CommentOperation(),
                AlertProperty.Value => condition is StringConditionViewModel ? new StringOperation() : new NumericOperation(),
                AlertProperty.Min or AlertProperty.Max or AlertProperty.Mean or AlertProperty.Count or AlertProperty.LastValue
                                  or AlertProperty.Length or AlertProperty.OriginalSize => new NumericOperation(),
                AlertProperty.NewSensorData or AlertProperty.Sensitivity or AlertProperty.TimeToLive => null,
                _ => throw new NotSupportedException(),
            };
    }
}
