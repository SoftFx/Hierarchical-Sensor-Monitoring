using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects.SensorRequests;
using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace HSMServer.BackgroundServices
{
    public class SlackChannelStatistics
    {
        private const string MessagesNode = "Messages";
        private const string Errors = "Errors";
        private const string NodeName = "Slack";
        private const string Rate = "Total";

        private readonly IDataCollector _collector;

        private readonly ConcurrentDictionary<string, IInstantValueSensor<string>> _notifications = new();
        private readonly IInstantValueSensor<string> _errors;
        private readonly IMonitoringRateSensor _messagesRate;

        private static readonly Regex LineBreakRegex = new(@"\r\n|\n|\r", RegexOptions.Compiled);


        public SlackChannelStatistics(IDataCollector collector)
        {
            _collector = collector;

            _errors = collector.CreateStringSensor($"{NodeName}/{Errors}", new InstantSensorOptions
            {
                Alerts = [],
                TTL = TimeSpan.MaxValue,
                EnableForGrafana = false,
                AggregateData = true,
                Description = $"The sensor sends information about {NodeName} errors."
            });

            _messagesRate = collector.CreateRateSensor($"{NodeName}/{Rate}", new RateSensorOptions()
            {
                Alerts = [],
                TTL = TimeSpan.MaxValue,
                EnableForGrafana = false,
                DisplayUnit = RateDisplayUnit.PerMinute,
                PostDataPeriod = TimeSpan.FromMinutes(1),
                Description = $"The sensor sends information about sent notifications to {NodeName}.",
            });
        }


        public void RegisterMessageSended(string destination, string message)
        {
            var sensor = _notifications.GetOrAdd(destination, id => _collector.CreateStringSensor($"{NodeName}/{MessagesNode}/{id}", new InstantSensorOptions
            {
                Alerts = [],
                TTL = TimeSpan.MaxValue,
                EnableForGrafana = false,
                Description = $"The sensor sends information about {NodeName} notifications messages sended."
            }));

            sensor.AddValue(LineBreakRegex.Replace(message, "<br>"));
        }

        public void RegisterMessageSending() => _messagesRate.AddValue(1);

        public void RegisterError(string message) => _errors.AddValue(message);
    }
}
