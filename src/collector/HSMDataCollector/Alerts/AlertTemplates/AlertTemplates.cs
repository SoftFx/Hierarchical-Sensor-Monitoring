using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using System;
using System.Collections.Generic;

namespace HSMDataCollector.Alerts
{
    public sealed class InstantAlertTemplate : AlertBaseTemplate { }


    public sealed class BarAlertTemplate : AlertBaseTemplate { }


    public sealed class SpecialAlertTemplate : AlertBaseTemplate
    {
        public TimeSpan? TtlValue { get; internal set; }
    }


    public abstract class AlertBaseTemplate
    {
        public List<AlertConditionTemplate> Conditions { get; set; }

        public SensorStatus Status { get; set; }


        public TimeSpan? ConfirmationPeriod { get; set; }

        public bool? SendScheduleFirstMessage { get; set; }

        public DateTime? ScheduledNotificationTime { get; set; }

        public AlertRepeatMode? ScheduledRepeatMode { get; set; }

        public string Template { get; set; }

        public string Icon { get; set; }


        public bool IsDisabled { get; set; }


        protected internal AlertBaseTemplate() { }
    }
}