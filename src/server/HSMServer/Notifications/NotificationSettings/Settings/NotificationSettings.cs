using HSMDatabase.AccessManager.DatabaseEntities;
using System;

namespace HSMServer.Notification.Settings
{
    public class NotificationSettings
    {
        private readonly Func<NotificationSettings> _getParent;


        public TelegramSettings UsedTelegram => IsCustom ? Telegram : _getParent?.Invoke()?.UsedTelegram ?? Telegram;

        public TelegramSettings Telegram { get; }

        public bool AutoSubscription { get; set; }


        internal bool IsCustom => Telegram.Inheritance == InheritedSettings.Custom;


        internal NotificationSettings()
        {
            Telegram = new();
        }

        internal NotificationSettings(NotificationSettingsEntity entity, Func<NotificationSettings> getParent = null)
        {
            _getParent = getParent;
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
