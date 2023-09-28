using HSMSensorDataObjects.SensorRequests;

namespace HSMDataCollector.Alerts
{
    public sealed class InstantAlertCondition : DataAlertCondition<InstantAlertTemplate>
    {
        internal InstantAlertCondition() : base() { }


        public InstantAlertCondition AndValue<T>(AlertOperation operation, T target)
        {
            BuildConstCondition(AlertProperty.Value, operation, target?.ToString());
            return this;
        }

        public InstantAlertCondition AndLength<T>(AlertOperation operation, T target)
        {
            BuildConstCondition(AlertProperty.Length, operation, target?.ToString());
            return this;
        }

        public InstantAlertCondition AndFileSize<T>(AlertOperation operation, T target)
        {
            BuildConstCondition(AlertProperty.OriginalSize, operation, target?.ToString());
            return this;
        }
    }
}