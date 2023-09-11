using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Notification.Settings
{
    public class NotificationSettings
    {
        public TelegramSettings Telegram { get; }


        internal NotificationSettings()
        {
            Telegram = new();
        }

        internal NotificationSettings(NotificationSettingsEntity entity)
        {
            Telegram = new(entity?.TelegramSettings);
        }


        public NotificationSettingsEntity ToEntity() =>
            new()
            {
                TelegramSettings = Telegram.ToEntity()
            };
    }
}
