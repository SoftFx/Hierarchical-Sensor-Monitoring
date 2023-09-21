using HSMDataCollector.Alerts;
using HSMDataCollector.Options;
using HSMPingModule.Settings;
using HSMSensorDataObjects.SensorRequests;

namespace HSMPingModule.PingServices
{
    internal class ResourceSensor : IDisposable
    {
        internal InstantSensorOptions SensorOptions { get; }

        internal PingAdapter PingAdapter { get; }


        internal string SensorPath { get; }

        internal string Country { get; }


        internal ResourceSensor(string host, string country, NodeSettings settings)
        {
            var timeout = settings.PingThresholdValue.Value.TotalMilliseconds * 2;

            PingAdapter = new PingAdapter(host, (int)timeout);

            SensorPath = $"{host}/{country}";
            Country = country;

            SensorOptions = new()
            {
                Description = $"This sensor receives ping timeout value from **{country}** to [**{host}**]({host})",

                TTL = settings.TTL,
                SensorUnit = Unit.Seconds,

                TtlAlert = AlertsFactory.IfInactivityPeriodIs().ThenSetIcon(AlertIcon.Clock).AndSendNotification("[$product]$path Ping timeout").Build(),
                Alerts = new List<InstantAlertTemplate>()
                {
                    AlertsFactory.IfValue(AlertOperation.GreaterThan, settings.PingThresholdValue).ThenSetIcon(AlertIcon.Warning).AndSendNotification("[$product]$path Ping $operation $target seconds").Build(),
                    AlertsFactory.IfStatus(AlertOperation.IsError).ThenSetIcon(AlertIcon.Error).AndSendNotification("[$product]$path $comment").Build(),
                }
            };
        }


        public void Dispose() => PingAdapter?.Dispose();
    }
}
