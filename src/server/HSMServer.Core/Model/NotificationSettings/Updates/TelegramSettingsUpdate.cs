namespace HSMServer.Core.Model
{
    public struct TelegramSettingsUpdate
    {
        public SensorStatus MessagesMinStatus { get; init; }

        public bool MessagesAreEnabled { get; init; }

        public int MessagesDelay { get; init; }
    }
}
