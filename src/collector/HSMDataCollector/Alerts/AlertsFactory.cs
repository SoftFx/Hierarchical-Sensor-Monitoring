using HSMSensorDataObjects.SensorRequests;
using System;

namespace HSMDataCollector.Alerts
{
    public abstract class AlertBuildRequest
    {
        protected internal AlertBuildRequest() { }
    }

    public sealed class SpecialAlertBuildRequest : AlertBuildRequest
    {
    }

    public sealed class InstantAlertBuildRequest : AlertBuildRequest
    {
    }

    public sealed class BarAlertBuildRequest : AlertBuildRequest
    {
    }


    public static class Alerts
    {
        public static SpecialSensorAlert IfInactivityPeriodIs(TimeSpan? time)
        {
            return new SpecialSensorAlert();
        }


        public static InstantSensorAlertCondition IfValue<T>(AlertOperation operation, T target)
        {
            return new InstantSensorAlertCondition();
        }

        public static InstantSensorAlertCondition IfComment(AlertOperation operation)
        {
            return new InstantSensorAlertCondition();
        }

        public static InstantSensorAlertCondition IfStatus(AlertOperation operation)
        {
            return new InstantSensorAlertCondition();
        }


        public static BarSensorAlertCondition IfMax<T>(AlertOperation operation, T value) where T : struct
        {
            return new BarSensorAlertCondition();
        }

        public static BarSensorAlertCondition IfMean<T>(AlertOperation operation, T value) where T : struct
        {
            return new BarSensorAlertCondition();
        }

        public static BarSensorAlertCondition IfMin<T>(AlertOperation operation, T value) where T : struct
        {
            return new BarSensorAlertCondition();
        }

        public static BarSensorAlertCondition IfLastValue<T>(AlertOperation operation, T value) where T : struct
        {
            return new BarSensorAlertCondition();
        }

        public static BarSensorAlertCondition IfBarComment(AlertOperation operation)
        {
            return new BarSensorAlertCondition();
        }

        public static BarSensorAlertCondition IfBarStatus(AlertOperation operation)
        {
            return new BarSensorAlertCondition();
        }
    }

    public sealed class AlertAction<T> where T : AlertBuildRequest, new()
    {
        internal AlertAction() { }


        public AlertAction<T> AndNotify(string template)
        {
            return this;
        }

        public AlertAction<T> AndSetSensorError()
        {
            return this;
        }

        public AlertAction<T> AndSetIcon(string icon)
        {
            return this;
        }

        public T Build()
        {
            return new T();
        }
    }

    public abstract class SensorAlert<T> where T : AlertBuildRequest, new()
    {

        public AlertAction<T> ThenNotify(string template)
        {
            return new AlertAction<T>();
        }

        public AlertAction<T> ThenSetSensorError()
        {
            return new AlertAction<T>();
        }

        public AlertAction<T> ThenSetIcon(string icon)
        {
            return new AlertAction<T>();
        }
    }

    public abstract class DataSensorAlertCondition<T> : SensorAlert<T>
         where T : AlertBuildRequest, new()
    {
        protected internal DataSensorAlertCondition() { }


        public DataSensorAlertCondition<T> AndComment(AlertOperation operation)
        {
            return this;
        }

        public DataSensorAlertCondition<T> AndStatus(AlertOperation operation)
        {
            return this;
        }
    }

    public sealed class InstantSensorAlertCondition : DataSensorAlertCondition<InstantAlertBuildRequest>
    {
        internal InstantSensorAlertCondition() : base() { }


        public InstantSensorAlertCondition AndValue<T>(AlertOperation operation, T value)
        {
            return this;
        }
    }

    public sealed class BarSensorAlertCondition : DataSensorAlertCondition<BarAlertBuildRequest>
    {
        internal BarSensorAlertCondition() : base() { }


        public BarSensorAlertCondition AndMax<T>(AlertOperation operation, T target) where T : struct
        {
            return this;
        }

        public BarSensorAlertCondition AndMean<T>(AlertOperation operation, T target) where T : struct
        {
            return this;
        }

        public BarSensorAlertCondition AndMin<T>(AlertOperation operation, T target) where T : struct
        {
            return this;
        }

        public BarSensorAlertCondition AndLastValue<T>(AlertOperation operation, T target) where T : struct
        {
            return this;
        }
    }

    public sealed class SpecialSensorAlert : SensorAlert<SpecialAlertBuildRequest>
    {
        internal SpecialSensorAlert() : base() { }
    }
}