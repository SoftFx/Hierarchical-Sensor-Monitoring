namespace HSMDataCollector.Alerts
{
    public sealed class AlertAction<T> where T : AlertBuildRequest, new()
    {
        internal AlertAction() { }


        public AlertAction<T> AndNotify(string template)
        {
            return this;
        }

        public AlertAction<T> AndSetIcon(string icon)
        {
            return this;
        }

        public AlertAction<T> AndSetSensorError()
        {
            return this;
        }

        public T Build()
        {
            return new T();
        }
    }
}