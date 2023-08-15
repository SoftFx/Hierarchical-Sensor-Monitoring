using HSMSensorDataObjects.SensorRequests;

namespace HSMDataCollector.Alerts
{
    public sealed class InstantAlertCondition : DataAlertCondition<InstantAlertBuildRequest>
    {
        internal InstantAlertCondition() : base() { }


        public InstantAlertCondition AndValue<T>(AlertOperation operation, T value)
        {
            return this;
        }
    }
}