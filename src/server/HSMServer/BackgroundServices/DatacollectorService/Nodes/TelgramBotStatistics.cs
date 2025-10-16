using System;
using System.Linq;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;


namespace HSMServer.BackgroundServices
{
    public record TelegramBotStatistics
    {
        private const string NotificationsMessages = "Messages";
        private const string Errors = "Errors";
        private const string NodeName = "Telegram Bot";

        private IInstantValueSensor<string> _notifications;
        private IInstantValueSensor<string> _errors;


        public TelegramBotStatistics(IDataCollector collector)
        {

            _notifications = collector.CreateStringSensor($"{NodeName}/{NotificationsMessages}", new InstantSensorOptions
            {
                Alerts = [],
                TTL = TimeSpan.MaxValue,
                EnableForGrafana = false,
                Description = $"The sensor sends information about {NodeName} notifications messages sended."
            });

            _errors = collector.CreateStringSensor($"{NodeName}/{Errors}", new InstantSensorOptions
            {
                Alerts = [],
                TTL = TimeSpan.MaxValue,
                EnableForGrafana = false,
                AggregateData = true,
                Description = $"The sensor sends information about {NodeName} errors."
            });
        }

        public void RegisterNotification(string message)
        {
            _notifications.AddValue(message);
        }

        public void RegisterError(string message)
        {
            _errors.AddValue(message);
        }
    }

}
