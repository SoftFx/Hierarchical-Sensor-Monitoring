namespace HSMServer.Core.Model.UserFilters
{
    public class GroupByNotifications : UserFilterGroupBase
    {
        internal override FilterProperty[] Properties => new[] { Enabled, Ignored };

        internal override FilterGroupType Type => FilterGroupType.ByNotifications;


        public FilterProperty Enabled { get; init; } = new(nameof(Enabled));

        public FilterProperty Ignored { get; init; } = new(nameof(Ignored));


        public GroupByNotifications() { }


        internal override bool IsSensorSuitable(FilteredSensor sensor) =>
            Enabled.Value == sensor.IsNotificationsEnabled ||
            Ignored.Value == sensor.IsNotificationsIgnored;
    }
}
