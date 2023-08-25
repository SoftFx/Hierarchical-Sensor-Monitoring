using HSMDataCollector.Alerts;
using HSMDataCollector.Options;
using HSMPingModule.Settings;
using HSMSensorDataObjects.SensorRequests;

namespace HSMPingModule.Resourses;

internal sealed class WebSite
{
    private readonly InstantSensorOptions _options;


    public List<string> Countries { get; set; }

    public TimeSpan? TTL { get; set; }

    public int? PingTimeoutValue { get; set; }
    
    public int? PingDelay { get; set; }

    public InstantSensorOptions GetOptions => _options;


    public WebSite(){}

    public WebSite(WebSite webSite)
    {
        Countries = webSite.Countries ?? ResourceSettings.DefaultSiteNodeSettings.Countries;
        TTL = webSite.TTL ?? ResourceSettings.DefaultSiteNodeSettings.TTL;
        PingTimeoutValue = webSite.PingTimeoutValue ?? ResourceSettings.DefaultSiteNodeSettings.PingTimeoutValue;
        PingDelay = webSite.PingDelay ?? ResourceSettings.DefaultSiteNodeSettings.PingDelay;
        _options = new()
        {
            TTL = TTL,
            TtlAlert = AlertsFactory.IfInactivityPeriodIs(TTL).ThenSetIcon("🎃").AndSendNotification("$product $path test").Build(),
            Alerts = new List<InstantAlertTemplate>()
            {
                AlertsFactory.IfValue(AlertOperation.GreaterThan, PingTimeoutValue).ThenSetIcon("🤣").AndSendNotification("$product $path ping timeout").Build(),
                AlertsFactory.IfStatus(AlertOperation.IsError).ThenSetIcon("❌").Build()
            }
        };
    }
}