using HSMDataCollector.Extensions;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using System;
using System.Collections.Generic;

namespace HSMDataCollector.Alerts
{
    public enum AlertIcon
    {
        Ok = 0,
        Warning = 1,
        Error = 2,
        Pause = 3,

        ArrowUp = 10,
        ArrowDown = 11,

        Clock = 100,
        Hourglass = 101,
    }


    public class AlertAction<T> where T : AlertBaseTemplate, new()
    {
        private readonly List<AlertConditionTemplate> _conditions;


        public TimeSpan? ConfirmationPeriod { get; }


        public bool? SendScheduleFirstMessage { get; private set; }

        public DateTime? ScheduledNotificationTime { get; private set; }

        public AlertRepeatMode? ScheduledRepeatMode { get; private set; }

        public SensorStatus Status { get; private set; } = SensorStatus.Ok;

        public string Template { get; private set; }

        public string Icon { get; private set; }


        public bool IsDisabled { get; private set; }


        internal AlertAction(List<AlertConditionTemplate> conditions, TimeSpan? confirmationPeriod)
        {
            _conditions = conditions;

            ConfirmationPeriod = confirmationPeriod;
        }


        public AlertAction<T> AndSendNotification(string template)
        {
            Template = template;

            return this;
        }

        public AlertAction<T> AndSendScheduledNotification(string template, DateTime time, AlertRepeatMode repeatMode, bool sendFirst)
        {
            Template = template;
            ScheduledNotificationTime = time;
            ScheduledRepeatMode = repeatMode;
            SendScheduleFirstMessage = sendFirst;

            return this;
        }

        public AlertAction<T> AndSetIcon(string icon)
        {
            Icon = icon;

            return this;
        }

        public AlertAction<T> AndSetIcon(AlertIcon icon)
        {
            Icon = icon.ToUtf8();

            return this;
        }

        public AlertAction<T> AndSetSensorError()
        {
            Status = SensorStatus.Error;

            return this;
        }

        public T BuildAndDisable()
        {
            IsDisabled = true;

            return Build();
        }

        public virtual T Build() => new T()
        {
            Conditions = _conditions,

            ConfirmationPeriod = ConfirmationPeriod,
            Template = Template,
            Status = Status,
            Icon = Icon,

            ScheduledRepeatMode = ScheduledRepeatMode,
            ScheduledNotificationTime = ScheduledNotificationTime,
            SendScheduleFirstMessage = SendScheduleFirstMessage,

            IsDisabled = IsDisabled
        };
    }
}