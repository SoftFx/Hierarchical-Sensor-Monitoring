using HSMSensorDataObjects.SensorRequests;
using System.Collections.Generic;

namespace HSMDataCollector.Alerts
{
    public abstract class AlertConditionBase<T> where T : AlertBuildRequest, new()
    {
        private readonly List<AlertConditionBuildRequest> _conditions = new List<AlertConditionBuildRequest>();


        public AlertAction<T> ThenNotify(string template) => BuildAlertAction().AndNotify(template);

        public AlertAction<T> ThenSetIcon(string icon) => BuildAlertAction().AndSetIcon(icon);

        public AlertAction<T> ThenSetSensorError() => BuildAlertAction().AndSetSensorError();


        protected virtual AlertAction<T> BuildAlertAction() => new AlertAction<T>(_conditions);

        protected void BuildCondition(AlertProperty property, AlertOperation operation, string value = null)
        {
            _conditions.Add(new AlertConditionBuildRequest()
            {
                Target = new AlertTargetBuildRequest()
                {
                    Type = string.IsNullOrEmpty(value) ? TargetType.LastValue : TargetType.Const,
                    Value = value,
                },

                Combination = AlertCombination.And,

                Operation = operation,
                Property = property,
            });
        }
    }


    public abstract class DataAlertCondition<T> : AlertConditionBase<T>
         where T : AlertBuildRequest, new()
    {
        protected internal DataAlertCondition() { }


        public DataAlertCondition<T> AndComment(AlertOperation operation)
        {
            BuildCondition(AlertProperty.Comment, operation);
            return this;
        }

        public DataAlertCondition<T> AndStatus(AlertOperation operation)
        {
            BuildCondition(AlertProperty.Status, operation);
            return this;
        }
    }
}