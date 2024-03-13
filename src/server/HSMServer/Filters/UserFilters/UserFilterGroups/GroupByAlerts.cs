namespace HSMServer.UserFilters
{
    public sealed class GroupByAlerts : UserFilterGroupBase
    {
        internal override FilterProperty[] Properties => new[] { HasUnconfiguredAlerts };

        internal override FilterGroupType Type => FilterGroupType.Alerts;

        public FilterProperty HasUnconfiguredAlerts { get; init; } = new("Unconfigured");


        public GroupByAlerts() { }


        internal override bool IsSensorSuitable(FilteredSensor sensor) => HasUnconfiguredAlerts.Value && sensor.HasUnconfiguredAlerts;
    }
}
