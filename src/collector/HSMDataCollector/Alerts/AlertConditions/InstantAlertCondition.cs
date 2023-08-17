using HSMSensorDataObjects.SensorRequests;

namespace HSMDataCollector.Alerts
{
    public sealed class InstantAlertCondition : DataAlertCondition<InstantAlertTemplate>
    {
        internal InstantAlertCondition() : base() { }


        public InstantAlertCondition AndValue<T>(AlertOperation operation, T target)
        {
            BuildCondition(AlertProperty.Value, operation, target.ToString());
            return this;
        }
    }
}