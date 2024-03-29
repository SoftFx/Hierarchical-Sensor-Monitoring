﻿using HSMSensorDataObjects.SensorRequests;

namespace HSMDataCollector.Alerts
{
    public sealed class BarAlertCondition : DataAlertCondition<BarAlertTemplate>
    {
        internal BarAlertCondition() : base() { }


        public BarAlertCondition AndMax<T>(AlertOperation operation, T target) where T : struct
        {
            BuildConstCondition(AlertProperty.Max, operation, target.ToString());
            return this;
        }

        public BarAlertCondition AndMean<T>(AlertOperation operation, T target) where T : struct
        {
            BuildConstCondition(AlertProperty.Mean, operation, target.ToString());
            return this;
        }

        public BarAlertCondition AndMin<T>(AlertOperation operation, T target) where T : struct
        {
            BuildConstCondition(AlertProperty.Min, operation, target.ToString());
            return this;
        }

        public BarAlertCondition AndFirstValue<T>(AlertOperation operation, T target) where T : struct
        {
            BuildConstCondition(AlertProperty.FirstValue, operation, target.ToString());
            return this;
        }

        public BarAlertCondition AndLastValue<T>(AlertOperation operation, T target) where T : struct
        {
            BuildConstCondition(AlertProperty.LastValue, operation, target.ToString());
            return this;
        }

        public BarAlertCondition AndCount(AlertOperation operation, int target)
        {
            BuildConstCondition(AlertProperty.Count, operation, target.ToString());
            return this;
        }

        public BarAlertCondition AndEmaMin(AlertOperation operation, double target)
        {
            BuildConstCondition(AlertProperty.EmaMin, operation, target.ToString());
            return this;
        }

        public BarAlertCondition AndEmaMax(AlertOperation operation, double target)
        {
            BuildConstCondition(AlertProperty.EmaMax, operation, target.ToString());
            return this;
        }

        public BarAlertCondition AndEmaMean(AlertOperation operation, double target)
        {
            BuildConstCondition(AlertProperty.EmaMean, operation, target.ToString());
            return this;
        }

        public BarAlertCondition AndEmaCount(AlertOperation operation, double target)
        {
            BuildConstCondition(AlertProperty.EmaCount, operation, target.ToString());
            return this;
        }
    }
}