using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Notification.Settings
{
    public class NotificationSettings
    {
        public TelegramSettings Telegram { get; }

        public bool AutoSubscription { get; set; } = true;


        internal NotificationSettings()
        {
            Telegram = new();
        }

        internal NotificationSettings(NotificationSettingsEntity entity)
        {
            Telegram = new(entity?.TelegramSettings);

            if (entity != null)
                AutoSubscription = entity.AutoSubscription;
        }


        public NotificationSettingsEntity ToEntity() =>
            new()
            {
                TelegramSettings = Telegram.ToEntity()
            };
    }
}
