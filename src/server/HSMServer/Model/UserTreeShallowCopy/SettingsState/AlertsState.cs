namespace HSMServer.Model.UserTreeShallowCopy
{
    public sealed class AlertsState
    {
        public bool IsAnyEnabled { get; private set; }


        public void CalculateState(bool isEnabled)
        {
            ChangeEnableState(isEnabled);
        }

        public void CalculateState(AlertsState state)
        {
            ChangeEnableState(state.IsAnyEnabled);
        }

        private void ChangeEnableState(bool isEnabled) =>
            IsAnyEnabled |= isEnabled;
    }
}
