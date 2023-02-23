namespace HSMServer.Core.Model.UserFilters
{
    public class GroupByNotifications : UserFilterGroupBase
    {
        private const string EnabledName = "Enabled";
        
        private const string IgnoredName = "Ignored";
        

        internal override FilterProperty[] Properties => new[] { GroupEnabled, AccountEnabled, GroupIgnored, AccountIgnored };

        internal override FilterGroupType Type => FilterGroupType.ByNotifications;
        

        public FilterProperty GroupEnabled { get; init; } = new(EnabledName);
        
        public FilterProperty AccountEnabled { get; init; } = new(EnabledName);

        public FilterProperty GroupIgnored { get; init; } = new(IgnoredName);

        public FilterProperty AccountIgnored { get; init; } = new(IgnoredName);


        public GroupByNotifications() { }


        internal override bool IsSensorSuitable(FilteredSensor sensor) =>
            (AccountEnabled.Value && AccountEnabled.Value == sensor.IsNotificationsAccountEnabled) || 
            (GroupEnabled.Value && GroupEnabled.Value == sensor.IsNotificationsGroupEnabled) ||
            (AccountIgnored.Value && AccountIgnored.Value == sensor.IsNotificationsAccountIgnored) || 
            (GroupIgnored.Value && GroupIgnored.Value == sensor.IsNotificationsGroupIgnored);
    }
}