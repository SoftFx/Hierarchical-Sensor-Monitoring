namespace HSMServer.Core.Model.UserFilters
{
    public class GroupByNotifications : UserFilterGroupBase
    {
        internal override FilterProperty[] Properties => new[] { Enabled, GroupIgnored, AccountIgnored };

        internal override FilterGroupType Type => FilterGroupType.ByNotifications;


        public FilterProperty Enabled { get; init; } = new(nameof(Enabled));

        public FilterProperty GroupIgnored { get; init; } = new("Ignored");

        public FilterProperty AccountIgnored { get; init; } = new("Ignored");


        public GroupByNotifications() { }


        internal override bool IsSensorSuitable(FilteredSensor sensor) =>
            Enabled.Value == sensor.IsNotificationsEnabled ||
            (AccountIgnored.Value && AccountIgnored.Value == sensor.IsNotificationsAccountIgnored) || 
            (GroupIgnored.Value && GroupIgnored.Value == sensor.IsNotificationsGroupIgnored);
    }
}