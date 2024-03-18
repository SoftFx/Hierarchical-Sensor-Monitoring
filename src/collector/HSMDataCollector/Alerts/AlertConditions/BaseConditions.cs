using HSMSensorDataObjects.SensorRequests;
using System;
using System.Collections.Generic;

namespace HSMDataCollector.Alerts
{
    public abstract class AlertConditionBase<T> where T : AlertBaseTemplate, new()
    {
        private readonly List<AlertConditionTemplate> _conditions = new List<AlertConditionTemplate>();
        private protected TimeSpan? _confirmationPeriod;


        public AlertAction<T> ThenSendNotification(string template) => BuildAlertAction().AndSendNotification(template);

        public AlertAction<T> ThenSendScheduledNotification(string template, DateTime time, AlertRepeatMode repeatMode, bool instantSend) =>
            BuildAlertAction().AndSendScheduledNotification(template, time, repeatMode, instantSend);

        public AlertAction<T> ThenSetIcon(string icon) => BuildAlertAction().AndSetIcon(icon);

        public AlertAction<T> ThenSetIcon(AlertIcon icon) => BuildAlertAction().AndSetIcon(icon);

        public AlertAction<T> ThenSetSensorError() => BuildAlertAction().AndSetSensorError();


        internal AlertAction<T> ThenSendInstantHourlyScheduledNotification(string template) =>
            ThenSendScheduledNotification(template, new DateTime(1, 1, 1, 12, 0, 0, DateTimeKind.Utc), AlertRepeatMode.Hourly, true);


        protected virtual AlertAction<T> BuildAlertAction() => new AlertAction<T>(_conditions, _confirmationPeriod);

        protected void BuildConstCondition(AlertProperty property, AlertOperation operation, string value) =>
            BuildCondition(property, operation, TargetType.Const, value);

        protected void BuildLastValueCondition(AlertProperty property, AlertOperation operation) =>
            BuildCondition(property, operation, TargetType.LastValue);

        private void BuildCondition(AlertProperty property, AlertOperation operation, TargetType target, string value = null)
        {
            _conditions.Add(new AlertConditionTemplate()
            {
                Target = new AlertTargetTemplate()
                {
                    Type = target,
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
            BuildLastValueCondition(AlertProperty.NewSensorData, AlertOperation.ReceivedNewValue);
            return this;
        }

        public DataAlertCondition<T> AndComment(AlertOperation operation, string target = null)
        {
            if (operation == AlertOperation.IsChanged)
                BuildLastValueCondition(AlertProperty.Comment, operation);
            else
                BuildConstCondition(AlertProperty.Comment, operation, target);

            return this;
        }

        public DataAlertCondition<T> AndStatus(AlertOperation operation)
        {
            BuildLastValueCondition(AlertProperty.Status, operation);
            return this;
        }


        public DataAlertCondition<T> AndConfirmationPeriod(TimeSpan period)
        {
            _confirmationPeriod = period;

            return this;
        }
    }
}