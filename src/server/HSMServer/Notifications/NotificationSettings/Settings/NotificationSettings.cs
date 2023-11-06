using HSMDatabase.AccessManager.DatabaseEntities;
using System;

namespace HSMServer.Notification.Settings
{
    [Obsolete("Should be removed after telegram chats migration")]
    public class NotificationSettings
    {
        public TelegramSettings Telegram { get; }


        internal NotificationSettings(NotificationSettingsEntity entity)
        {
            Telegram = new(entity?.TelegramSettings);
        }
    }
}
