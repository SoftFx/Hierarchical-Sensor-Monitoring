namespace HSMServer.Core.Model.UserFilters
{
    public class GroupByNotifications : UserFilterGroupBase
    {
        private const string Enabled = "Enabled";
        
        private const string Ignored = "Ignored";
        

        internal override FilterProperty[] Properties => new[] { GroupEnabled, AccountEnabled, GroupIgnored, AccountIgnored };

        internal override FilterGroupType Type => FilterGroupType.ByNotifications;
        

        public FilterProperty GroupEnabled { get; init; } = new($"{Enabled} Groups");
        
        public FilterProperty AccountEnabled { get; init; } = new($"{Enabled} Accounts");

        public FilterProperty GroupIgnored { get; init; } = new($"{Ignored} Groups");

        public FilterProperty AccountIgnored { get; init; } = new($"{Ignored} Accounts");


        public GroupByNotifications() { }


        internal override bool IsSensorSuitable(FilteredSensor sensor) =>
            (AccountEnabled.Value && AccountEnabled.Value == sensor.IsNotificationsAccountEnabled) || 
            (GroupEnabled.Value && GroupEnabled.Value == sensor.IsNotificationsGroupEnabled) ||
            (AccountIgnored.Value && AccountIgnored.Value == sensor.IsNotificationsAccountIgnored) || 
            (GroupIgnored.Value && GroupIgnored.Value == sensor.IsNotificationsGroupIgnored);
    }
}