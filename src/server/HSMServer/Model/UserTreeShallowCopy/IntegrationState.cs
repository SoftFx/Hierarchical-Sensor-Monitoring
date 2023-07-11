namespace HSMServer.Model.UserTreeShallowCopy
{
    public sealed class IntegrationState
    {
        public bool IsAllEnabled { get; private set; } = true;


        public void CalculateState(SensorShallowModel sensor)
        {
            ChangeEnableState(sensor.IsGrafanaEnabled);
        }

        public void CalculateState(IntegrationState state)
        {
            ChangeEnableState(state.IsAllEnabled);
        }

        private void ChangeEnableState(bool isEnabled) =>
            IsAllEnabled &= isEnabled;
    }
}
