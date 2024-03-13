namespace HSMServer.Model.UserTreeShallowCopy
{
    public sealed class AlertsState
    {
        public bool IsAnyEnabled { get; private set; }


        public void CalculateState(SensorShallowModel sensor)
        {
            ChangeEnableState(sensor.HasUnconfiguredAlerts);
        }

        public void CalculateState(AlertsState state)
        {
            ChangeEnableState(state.IsAnyEnabled);
        }

        private void ChangeEnableState(bool isEnabled) =>
            IsAnyEnabled |= isEnabled;
    }
}
