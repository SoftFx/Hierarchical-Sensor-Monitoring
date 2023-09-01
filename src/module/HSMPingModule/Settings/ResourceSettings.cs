using HSMPingModule.Resourses;

namespace HSMPingModule.Settings;

internal sealed class ResourceSettings
{
    public Dictionary<string, WebSite> WebSites { get; set; } = new();

    public WebSite DefaultSiteNodeSettings { get; set; } = new ();


    public ResourceSettings ApplyDefaultSettings()
    {
        foreach (var (_, value) in WebSites)
            ApplyDefaultSettings(value);

        return this;

        void ApplyDefaultSettings(WebSite currentWebSite)
        {
            currentWebSite.PingDelay ??= DefaultSiteNodeSettings.PingDelay;
            currentWebSite.PingTimeoutValue ??= DefaultSiteNodeSettings.PingTimeoutValue;
            currentWebSite.Countries ??= DefaultSiteNodeSettings.Countries;
            currentWebSite.TTL ??= DefaultSiteNodeSettings.TTL;
        }
    }
}