using HSMSensorDataObjects.SensorRequests;

namespace HSMDataCollector.Alerts
{
    public sealed class BarAlertCondition : DataAlertCondition<BarAlertBuildRequest>
    {
        internal BarAlertCondition() : base() { }


        public BarAlertCondition AndMax<T>(AlertOperation operation, T target) where T : struct
        {
            return this;
        }

        public BarAlertCondition AndMean<T>(AlertOperation operation, T target) where T : struct
        {
            return this;
        }

        public BarAlertCondition AndMin<T>(AlertOperation operation, T target) where T : struct
        {
            return this;
        }

        public BarAlertCondition AndLastValue<T>(AlertOperation operation, T target) where T : struct
        {
            return this;
        }
    }
}
