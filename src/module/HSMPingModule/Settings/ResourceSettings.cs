using HSMPingModule.Models;

namespace HSMPingModule.Settings;

internal sealed class ResourceSettings
{
    public Dictionary<string, WebSite> WebSites { get; set; } = new();

    public WebSite DefaultSiteNodeSettings { get; set; } = new()
    {
        TTL = TimeSpan.FromMinutes(15),
        PingErrorValue = 15,
        PingRequestDelaySec = 15,
    };


    public ResourceSettings ApplyDefaultSettings()
    {
        foreach (var (_, value) in WebSites)
        {
            value.PingRequestDelaySec = DefaultSiteNodeSettings.PingRequestDelaySec;
            value.PingErrorValue ??= DefaultSiteNodeSettings.PingErrorValue;
            value.Countries ??= DefaultSiteNodeSettings.Countries;
            value.TTL ??= DefaultSiteNodeSettings.TTL;
        }

        return this;
    }
}