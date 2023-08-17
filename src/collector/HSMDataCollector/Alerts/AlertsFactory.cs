using HSMSensorDataObjects.SensorRequests;
using System;

namespace HSMDataCollector.Alerts
{
    public static class Alert
    {
        public static SpecialAlertCondition IfInactivityPeriodIs(TimeSpan? time = null)
        {
            return new SpecialAlertCondition().AddTtlValue(time);
        }


        public static InstantAlertCondition IfValue<T>(AlertOperation operation, T target)
        {
            return new InstantAlertCondition().AndValue(operation, target);
        }

        public static InstantAlertCondition IfComment(AlertOperation operation)
        {
            return (InstantAlertCondition)new InstantAlertCondition().AndComment(operation);
        }

        public static InstantAlertCondition IfStatus(AlertOperation operation)
        {
            return (InstantAlertCondition)new InstantAlertCondition().AndStatus(operation);
        }


        public static BarAlertCondition IfMax<T>(AlertOperation operation, T value) where T : struct
        {
            return new BarAlertCondition().AndMax(operation, value);
        }

        public static BarAlertCondition IfMean<T>(AlertOperation operation, T value) where T : struct
        {
            return new BarAlertCondition().AndMean(operation, value);
        }

        public static BarAlertCondition IfMin<T>(AlertOperation operation, T value) where T : struct
        {
            return new BarAlertCondition().AndMin(operation, value);
        }

        public static BarAlertCondition IfLastValue<T>(AlertOperation operation, T value) where T : struct
        {
            return new BarAlertCondition().AndLastValue(operation, value);
        }

        public static BarAlertCondition IfBarComment(AlertOperation operation)
        {
            return (BarAlertCondition)new BarAlertCondition().AndComment(operation);
        }

        public static BarAlertCondition IfBarStatus(AlertOperation operation)
        {
            return (BarAlertCondition)new BarAlertCondition().AndComment(operation).AndStatus(operation);
        }
    }
}