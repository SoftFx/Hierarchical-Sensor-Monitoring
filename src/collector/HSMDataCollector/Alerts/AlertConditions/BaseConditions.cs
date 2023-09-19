using HSMSensorDataObjects.SensorRequests;
using System.Collections.Generic;

namespace HSMDataCollector.Alerts
{
    public abstract class AlertConditionBase<T> where T : AlertBaseTemplate, new()
    {
        private readonly List<AlertConditionTemplate> _conditions = new List<AlertConditionTemplate>();


        public AlertAction<T> ThenSendNotification(string template) => BuildAlertAction().AndSendNotification(template);

        public AlertAction<T> ThenSetIcon(string icon) => BuildAlertAction().AndSetIcon(icon);

        public AlertAction<T> ThenSetIcon(AlertIcon icon) => BuildAlertAction().AndSetIcon(icon);

        public AlertAction<T> ThenSetSensorError() => BuildAlertAction().AndSetSensorError();


        protected virtual AlertAction<T> BuildAlertAction() => new AlertAction<T>(_conditions);

        protected void BuildCondition(AlertProperty property, AlertOperation operation, string value = null)
        {
            _conditions.Add(new AlertConditionTemplate()
            {
                Target = new AlertTargetTemplate()
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
         where T : AlertBaseTemplate, new()
    {
        protected internal DataAlertCondition() { }


        public DataAlertCondition<T> AndReceivedNewValue()
        {
            BuildCondition(AlertProperty.NewSensorData, AlertOperation.ReceivedNewValue);
            return this;
        }

        public DataAlertCondition<T> AndComment(AlertOperation operation, string target = null)
        {
            BuildCondition(AlertProperty.Comment, operation, target);
            return this;
        }

        public DataAlertCondition<T> AndStatus(AlertOperation operation)
        {
            BuildCondition(AlertProperty.Status, operation);
            return this;
        }
    }
}