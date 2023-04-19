using HSMServer.Core.Model;

namespace HSMServer.Notification.Settings
{
    public readonly struct TelegramMessagesSettingsUpdate
    {
        public InheritedSettings Inheritance { get; init; }

        public SensorStatus MinStatus { get; init; }

        public bool Enabled { get; init; }

        public int Delay { get; init; }
    }
}
