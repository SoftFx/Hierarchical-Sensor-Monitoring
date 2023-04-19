using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Notification.Settings
{
    public class NotificationSettings
    {
        private readonly NotificationSettings _parent;
        private readonly TelegramSettings _telegram;


        public TelegramSettings Telegram => _telegram.IsCustom ? _telegram : _parent?.Telegram ?? _telegram;


        internal NotificationSettings()
        {
            _telegram = new();
        }

        internal NotificationSettings(NotificationSettingsEntity entity, NotificationSettings parent = null)
        {
            _parent = parent;
            _telegram = new(entity?.TelegramSettings);
        }

        public NotificationSettingsEntity ToEntity() => new() { TelegramSettings = Telegram.ToEntity() };
    }
}
