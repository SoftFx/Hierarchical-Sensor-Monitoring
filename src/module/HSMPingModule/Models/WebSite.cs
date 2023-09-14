using HSMDataCollector.Alerts;
using HSMDataCollector.Options;
using HSMSensorDataObjects.SensorRequests;

namespace HSMPingModule.Models;

internal sealed class WebSite
{
    public List<string> Countries { get; set; }

    public TimeSpan? TTL { get; set; }

    public int? PingTimeoutValue { get; set; }

    public int? PingRequestDelaySec { get; set; }


    public InstantSensorOptions GetOptions(string country, string hostname, int delay)
    {
        return new()
        {
            TTL = TTL,
            SensorUnit = Unit.Seconds,

            TtlAlert = AlertsFactory.IfInactivityPeriodIs().ThenSetIcon(AlertIcon.Clock).AndSendNotification("[$product]$path Ping timeout").Build(),
            Alerts = new List<InstantAlertTemplate>()
            {
                AlertsFactory.IfValue(AlertOperation.GreaterThan, PingTimeoutValue).ThenSetIcon(AlertIcon.Warning).AndSendNotification("[$product]$path Ping $operation $target seconds").Build(),
                AlertsFactory.IfStatus(AlertOperation.IsError).ThenSetIcon(AlertIcon.Error).AndSendNotification("[$product]$path $comment").Build(),
            },
            Description = $"This sensor receives ping timeout value from **{country}** to **{hostname}** every **{delay}** minutes"
        };
    }
}