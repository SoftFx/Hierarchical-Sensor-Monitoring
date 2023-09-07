using System.Text.Json.Serialization;
using HSMDataCollector.Alerts;
using HSMDataCollector.Options;
using HSMSensorDataObjects.SensorRequests;

namespace HSMPingModule.Models;

internal sealed class WebSite
{
    public List<string> Countries { get; set; } = new ();

    public TimeSpan? TTL { get; set; } = TimeSpan.FromMinutes(15);


    public int? PingTimeoutValue { get; set; } = 15;

    public int? PingDelay { get; set; } = 15;


    [JsonIgnore]
    public InstantSensorOptions GetOptions => new()
    {
        TTL = TTL,
        SensorUnit = Unit.Seconds,

        TtlAlert = AlertsFactory.IfInactivityPeriodIs().ThenSetIcon(AlertIcon.Clock).AndSendNotification("$product $path test").Build(),
        Alerts = new List<InstantAlertTemplate>()
        {
            AlertsFactory.IfValue(AlertOperation.GreaterThan, PingTimeoutValue).ThenSetIcon(AlertIcon.Warning).AndSendNotification("$product $path ping timeout").Build(),
            AlertsFactory.IfStatus(AlertOperation.IsError).ThenSetIcon(AlertIcon.Error).Build(),
        },
    };
}