namespace HSMPingModule.Settings;

internal sealed class ResourceSettings
{
    public Dictionary<string, NodeSettings> WebSites { get; set; } = new();

    public NodeSettings DefaultSiteNodeSettings { get; set; } = new()
    {
        TTL = TimeSpan.FromMinutes(15),
        PingThresholdValue = 15,
    };


    public ResourceSettings ApplyDefaultSettings()
    {
        foreach (var (_, value) in WebSites)
        {
            value.PingThresholdValue ??= DefaultSiteNodeSettings.PingThresholdValue;
            value.Countries ??= DefaultSiteNodeSettings.Countries;
            value.TTL ??= DefaultSiteNodeSettings.TTL;
        }

        return this;
    }
}