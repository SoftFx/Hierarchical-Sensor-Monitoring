namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public sealed class NotificationSettingsEntity
    {
        public TelegramSettingsEntity TelegramSettings { get; init; }
    }


    public sealed class TelegramSettingsEntity
    {
        public byte MessagesMinStatus { get; init; }

        public bool MessagesAreEnabled { get; init; }

        public int MessagesDelay { get; init; }

        public long? ChatIdentifier { get; init; }
    }
}
