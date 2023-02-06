namespace HSMServer.Core.Model.UserFilters
{
    public class GroupByNotifications : UserFilterGroupBase
    {
        internal override FilterProperty[] Properties => new[] { Enabled, Ignored };

        internal override FilterGroupType Type => FilterGroupType.ByNotifications;


        public FilterProperty Enabled { get; init; } = new("Enabled");

        public FilterProperty Ignored { get; init; } = new("Ignored");


        public GroupByNotifications() { }


        internal override bool IsSensorSuitable(FilteredSensor sensor) =>
            Enabled.Value == sensor.IsNotificationsEnabled ||
            Ignored.Value == sensor.IsNotificationsIgnored;
    }
}
