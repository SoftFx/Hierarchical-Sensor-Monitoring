using HSMPingModule.Models;

namespace HSMPingModule.Settings;

internal sealed class ResourceSettings
{
    public Dictionary<string, WebSite> WebSites { get; set; } = new();

    public WebSite DefaultSiteNodeSettings { get; set; } = new();


    public ResourceSettings ApplyDefaultSettings()
    {
        foreach (var (_, value) in WebSites)
        {
            value.PingDelay ??= DefaultSiteNodeSettings.PingDelay;
            value.PingTimeoutValue ??= DefaultSiteNodeSettings.PingTimeoutValue;
            value.Countries ??= DefaultSiteNodeSettings.Countries;
            value.TTL ??= DefaultSiteNodeSettings.TTL;
        }

        return this;
    }
}