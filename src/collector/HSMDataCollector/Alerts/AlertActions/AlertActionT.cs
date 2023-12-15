using HSMDataCollector.Extensions;
using HSMSensorDataObjects;
using System;
using System.Collections.Generic;
using HSMSensorDataObjects.SensorRequests;

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


        public DateTime? ScheduledNotificationTime { get; private set; }
        
        public AlertRepeatMode ScheduledRepeatMode { get; private set; }

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

        public AlertAction<T> ScheduleNotificationTime(DateTime? time)
        {
            ScheduledNotificationTime = time;
            
            return this;
        }
        
        public AlertAction<T> ScheduleRepeatMode(AlertRepeatMode repeatMode)
        {
            ScheduledRepeatMode = repeatMode;
            
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

            IsDisabled = IsDisabled
        };
    }
}