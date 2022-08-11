using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMServer.Core.Model
{
    public sealed class NotificationSettings
    {
        public TelegramSettings TelegramSettings { get; internal set; }


        internal NotificationSettings()
        {
            TelegramSettings = new();
        }

        internal NotificationSettings(NotificationSettingsEntity entity)
        {
            TelegramSettings = new(entity?.TelegramSettings);
        }


        internal NotificationSettingsEntity ToEntity() =>
            new()
            {
                TelegramSettings = TelegramSettings.ToEntity(),
            };
    }
}
