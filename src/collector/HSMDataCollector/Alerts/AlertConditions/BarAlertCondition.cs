using HSMSensorDataObjects.SensorRequests;

namespace HSMDataCollector.Alerts
{
    public sealed class BarAlertCondition : DataAlertCondition<BarAlertTemplate>
    {
        internal BarAlertCondition() : base() { }


        public BarAlertCondition AndMax<T>(AlertOperation operation, T target) where T : struct
        {
            BuildCondition(AlertProperty.Max, operation, target.ToString());
            return this;
        }

        public BarAlertCondition AndMean<T>(AlertOperation operation, T target) where T : struct
        {
            BuildCondition(AlertProperty.Mean, operation, target.ToString());
            return this;
        }

        public BarAlertCondition AndMin<T>(AlertOperation operation, T target) where T : struct
        {
            BuildCondition(AlertProperty.Min, operation, target.ToString());
            return this;
        }

        public BarAlertCondition AndLastValue<T>(AlertOperation operation, T target) where T : struct
        {
            BuildCondition(AlertProperty.LastValue, operation, target.ToString());
            return this;
        }

        public BarAlertCondition AndCount(AlertOperation operation, int target)
        {
            BuildCondition(AlertProperty.Count, operation, target.ToString());
            return this;
        }
    }
}