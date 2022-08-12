namespace HSMServer.Core.Model
{
    public readonly struct TelegramMessagesSettingsUpdate
    {
        public SensorStatus MinStatus { get; init; }

        public bool Enabled { get; init; }

        public int Delay { get; init; }
    }
}
