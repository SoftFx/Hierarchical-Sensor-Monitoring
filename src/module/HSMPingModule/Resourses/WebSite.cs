using HSMDataCollector.Alerts;
using HSMDataCollector.Core;
using HSMDataCollector.Extensions;
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
            TtlAlert = AlertsFactory.IfInactivityPeriodIs().ThenSetIcon(AlertIcon.Clock.ToUtf8()).AndSendNotification("$product $path test").Build(),
            Alerts = new List<InstantAlertTemplate>()
            {
                AlertsFactory.IfValue(AlertOperation.GreaterThan, PingTimeoutValue).ThenSetIcon(AlertIcon.Warning.ToUtf8()).AndSendNotification("$product $path ping timeout").Build(),
                AlertsFactory.IfStatus(AlertOperation.IsError).ThenSetIcon("❌").Build()
            },
            SensorUnit = Unit.Seconds
        };
    }
    
    public bool Equals(WebSite other)
    {
        return TTL.Value == other.TTL.Value && PingTimeoutValue.Value == other.PingTimeoutValue.Value && PingDelay.Value == other.PingDelay.Value;
    }
}