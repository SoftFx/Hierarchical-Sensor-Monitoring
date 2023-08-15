using HSMSensorDataObjects.SensorRequests;

namespace HSMDataCollector.Alerts
{
    public abstract class AlertConditionBase<T> where T : AlertBuildRequest, new()
    {
        public AlertAction<T> ThenNotify(string template)
        {
            return new AlertAction<T>();
        }

        public AlertAction<T> ThenSetIcon(string icon)
        {
            return new AlertAction<T>();
        }

        public AlertAction<T> ThenSetSensorError()
        {
            return new AlertAction<T>();
        }
    }


    public abstract class DataAlertCondition<T> : AlertConditionBase<T>
         where T : AlertBuildRequest, new()
    {
        protected internal DataAlertCondition() { }


        public DataAlertCondition<T> AndComment(AlertOperation operation)
        {
            return this;
        }

        public DataAlertCondition<T> AndStatus(AlertOperation operation)
        {
            return this;
        }
    }
}