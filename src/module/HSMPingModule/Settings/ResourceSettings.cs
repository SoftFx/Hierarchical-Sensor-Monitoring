namespace HSMPingModule.Settings;

internal sealed class ResourceSettings
{
    public Dictionary<string, NodeSettings> WebSites { get; set; } = new()
    {
        ["google.com"] = new NodeSettings()
    };

    public NodeSettings DefaultSiteNodeSettings { get; set; } = new()
    {
        Countries = new HashSet<string>() { "Latvia" },

        PingThresholdValue = TimeSpan.FromSeconds(15),
        TTL = TimeSpan.FromMinutes(15),
    };


    public ResourceSettings ApplyDefaultSettings()
    {
        foreach (var (_, value) in WebSites)
        {
            value.Countries ??= new HashSet<string>(DefaultSiteNodeSettings.Countries);

            value.PingThresholdValue ??= DefaultSiteNodeSettings.PingThresholdValue;
            value.TTL ??= DefaultSiteNodeSettings.TTL;
        }

        return this;
    }
}