using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Core.Model
{
    public sealed class NotificationSettings
    {
        public TelegramSettings Telegram { get; set; }


        internal NotificationSettings()
        {
            Telegram = new();
        }

        internal NotificationSettings(NotificationSettingsEntity entity)
        {
            Telegram = new(entity?.TelegramSettings);
        }


        internal NotificationSettingsEntity ToEntity() =>
            new()
            {
                TelegramSettings = Telegram.ToEntity(),
            };
    }
}
