namespace HSMServer.Core.Model
{
    public class NodeStateViewModel
    {
        public bool IsAnyNotificationsEnabled { get; private set; }

        public bool IsAllNotificationsEnabled { get; private set; }

        public bool IsAllNotificationsIgnored { get; private set; }

        public int FilteredSensorsCount { get; private set; }


        public NodeStateViewModel() => Reset();


        public void ChangeEnableState(bool isNotificationsEnabled)
        {
            IsAnyNotificationsEnabled |= isNotificationsEnabled;
            IsAllNotificationsEnabled &= isNotificationsEnabled;
        }

        public void ChangeIgnoreState(bool isNotificationsIgnored) =>
            IsAllNotificationsIgnored &= isNotificationsIgnored;

        public void ChangeSensorsCount(int visibleSensors) =>
            FilteredSensorsCount += visibleSensors;

        public void AddChildState(NodeStateViewModel childState)
        {
            ChangeSensorsCount(childState.FilteredSensorsCount);
            ChangeEnableState(childState.IsAnyNotificationsEnabled);
            ChangeIgnoreState(childState.IsAllNotificationsIgnored);
        }

        internal void Reset()
        {
            IsAnyNotificationsEnabled = false;
            IsAllNotificationsEnabled = true;
            IsAllNotificationsIgnored = true;

            FilteredSensorsCount = 0;
        }
    }
}
