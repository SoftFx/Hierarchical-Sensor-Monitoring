using HSMDatabase.AccessManager.DatabaseEntities;
using System;

namespace HSMServer.Notification.Settings
{
    [Obsolete("Should be removed after telegram chats migration")]
    public class ClientNotifications : NotificationSettings
    {
        [Obsolete("Should be removed after telegram chats migration")]
        internal ClientNotifications(NotificationSettingsEntity entity) : base(entity) { }
    }
}
