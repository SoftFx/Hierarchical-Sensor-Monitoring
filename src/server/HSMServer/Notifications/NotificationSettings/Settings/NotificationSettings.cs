using HSMDatabase.AccessManager.DatabaseEntities;
using System;

namespace HSMServer.Notification.Settings
{
    [Obsolete("Should be removed after telegram chats migration")]
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
