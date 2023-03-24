namespace HSMServer.UserFilters
{
    public class GroupByNotifications : UserFilterGroupBase
    {
        internal override FilterProperty[] Properties => new[] { GroupEnabled, AccountEnabled, GroupIgnored, AccountIgnored };

        internal override FilterGroupType Type => FilterGroupType.ByNotifications;
        

        public FilterProperty GroupEnabled { get; init; } = new("Enabled Groups");
        
        public FilterProperty AccountEnabled { get; init; } = new("Enabled Accounts");

        public FilterProperty GroupIgnored { get; init; } = new("Ignored Groups");

        public FilterProperty AccountIgnored { get; init; } = new("Ignored Accounts");


        public GroupByNotifications() { }


        internal override bool IsSensorSuitable(FilteredSensor sensor) =>
            (AccountEnabled.Value && sensor.IsAccountNotificationsEnabled) || 
            (GroupEnabled.Value && sensor.IsGroupNotificationsEnabled) ||
            (AccountIgnored.Value && sensor.IsAccountNotificationsIgnored) || 
            (GroupIgnored.Value && sensor.IsGroupNotificationsIgnored);
    }
}