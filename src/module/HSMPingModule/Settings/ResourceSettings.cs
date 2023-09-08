using HSMPingModule.Models;

namespace HSMPingModule.Settings;

internal sealed class ResourceSettings
{
    public Dictionary<string, WebSite> WebSites { get; set; } = new();

    public WebSite DefaultSiteNodeSettings { get; set; } = new()
    {
        TTL = TimeSpan.FromMinutes(15),
        PingTimeoutValue = 15,
        PingDelay = 15,
        Countries = new List<string>
        {
            "Belarus",
            "USA",
        }
    };


    public ResourceSettings ApplyDefaultSettings()
    {
        DefaultSiteNodeSettings.Countries = DefaultSiteNodeSettings.Countries.Distinct().ToList();

        foreach (var (_, value) in WebSites)
        {
            value.PingDelay = DefaultSiteNodeSettings.PingDelay;
            value.PingTimeoutValue ??= DefaultSiteNodeSettings.PingTimeoutValue;
            value.Countries ??= DefaultSiteNodeSettings.Countries;
            value.TTL ??= DefaultSiteNodeSettings.TTL;
        }

        return this;
    }
}