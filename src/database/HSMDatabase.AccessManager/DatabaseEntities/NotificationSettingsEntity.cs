namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public sealed class NotificationSettingsEntity
    {
        public TelegramSettingsEntity TelegramSettings { get; init; }
    }


    public sealed class TelegramSettingsEntity
    {
        public byte TelegramMessagesMinStatus { get; init; }

        public bool EnableTelegramMessages { get; init; }

        public int TelegramMessagesDelay { get; init; }

        public long? ChatIdentifier { get; init; }
    }
}
