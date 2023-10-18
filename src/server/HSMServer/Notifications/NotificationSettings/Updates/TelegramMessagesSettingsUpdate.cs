using HSMServer.Core.Model;

namespace HSMServer.Notification.Settings
{
    public record TelegramMessagesSettingsUpdate
    {
        public SensorStatus? MinStatus { get; init; }

        public bool? Enabled { get; init; }

        public int? Delay { get; init; }
    }
}