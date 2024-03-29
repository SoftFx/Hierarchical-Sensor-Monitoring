﻿using HSMDataCollector.Alerts;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMPingModule.PingServices;
using HSMPingModule.Settings;
using HSMSensorDataObjects.SensorRequests;

namespace HSMPingModule.SensorStructure
{
    internal class ResourceSensor : IDisposable
    {
        internal InstantSensorOptions SensorOptions { get; }

        internal PingAdapter PingAdapter { get; }


        internal string SensorPath { get; }

        internal string Country { get; }


        internal ResourceSensor(string host, string country, NodeSettings settings, TimeSpan requestPeriod)
        {
            var timeout = TimeSpan.FromSeconds(settings.PingThresholdValueSec.Value + 1).TotalMilliseconds;

            PingAdapter = new PingAdapter(host, (int)timeout);

            SensorPath = $"{host.Replace('/', '_')}/{country}";
            Country = country;

            SensorOptions = new()
            {
                Description = $"This sensor receives ping timeout value from **{country}** to [**{host}**]({new UriBuilder(host)}).\n  " +
                $"Ping requests are send every {requestPeriod.ToReadableView()}",

                TTL = settings.TTL,
                SensorUnit = Unit.Seconds,

                TtlAlert = AlertsFactory.IfInactivityPeriodIs().ThenSetIcon(AlertIcon.Clock).AndSendNotification("[$product]$path Ping timeout").Build(),
                Alerts = new List<InstantAlertTemplate>()
                {
                    AlertsFactory.IfValue(AlertOperation.GreaterThan, settings.PingThresholdValueSec).ThenSetIcon(AlertIcon.Warning).AndSendNotification("[$product]$path Ping $operation $target seconds").Build(),
                    AlertsFactory.IfStatus(AlertOperation.IsError).ThenSetIcon(AlertIcon.Error).AndSendNotification("[$product]$path $comment").Build(),
                }
            };
        }


        public void Dispose() => PingAdapter?.Dispose();
    }
}