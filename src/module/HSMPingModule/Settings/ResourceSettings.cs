using HSMPingModule.Resourses;

namespace HSMPingModule.Settings;

internal sealed class ResourceSettings
{
    public Dictionary<string, WebSite> WebSites { get; set; }

    public static WebSite DefaultSiteNodeSettings { get; set; }


    public ResourceSettings(){}
    
    public ResourceSettings(ResourceSettings settings)
    {
        WebSites = new Dictionary<string, WebSite>(settings.WebSites.ToDictionary(x => x.Key, y => new WebSite(y.Value)));
    }
}