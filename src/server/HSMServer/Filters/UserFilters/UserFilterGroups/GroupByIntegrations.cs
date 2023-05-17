namespace HSMServer.UserFilters;

public class GroupByIntegrations : UserFilterGroupBase
{
    internal override FilterProperty[] Properties => new[] { GrafanaEnabled };
    
    internal override FilterGroupType Type => FilterGroupType.Integrations;

    public FilterProperty GrafanaEnabled { get; init; } = new("Grafana");

    
    public GroupByIntegrations() { }
    
    
    internal override bool IsSensorSuitable(FilteredSensor sensor) => GrafanaEnabled.Value && sensor.IsGrafanaEnabled;
}