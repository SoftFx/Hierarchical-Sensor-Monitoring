using HSMDataCollector.Alerts;
using HSMDataCollector.Options;
using HSMPingModule.Services;
using HSMSensorDataObjects.SensorRequests;

namespace HSMPingModule.Resourses;

internal sealed class WebSite
{
    private readonly InstantSensorOptions _options;


    public string HostName { get; set; }

    public List<string> Countries { get; set; }

    public TimeSpan TTL { get; set; } = TimeSpan.FromMinutes(1);

    public int PingTimeoutValue { get; set; } = PingService.SensorPingTimout;

    public InstantSensorOptions GetOptions => _options;
    

    public WebSite()
    {
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