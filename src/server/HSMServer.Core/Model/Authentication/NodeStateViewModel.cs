namespace HSMServer.Core.Model
{
    public class NodeStateViewModel
    {
        public bool IsAnyNotificationsEnabled { get; private set; }

        public bool IsAllNotificationsEnabled { get; private set; }

        public bool IsAnyNotificationsIgnored { get; private set; }

        public int FilteredSensorsCount { get; set; }


        public NodeStateViewModel() => Reset();


        public void ChangeEnableState(bool isNotificationsEnabled)
        {
            IsAnyNotificationsEnabled |= isNotificationsEnabled;
            IsAllNotificationsEnabled &= isNotificationsEnabled;
        }

        public void ChangeIgnoreState(bool isNotificationsIgnored)
        {
            IsAnyNotificationsIgnored &= isNotificationsIgnored;
        }

        internal void Reset()
        {
            IsAnyNotificationsEnabled = false;
            IsAllNotificationsEnabled = true;
            IsAnyNotificationsIgnored = true;
        }
    }
}
