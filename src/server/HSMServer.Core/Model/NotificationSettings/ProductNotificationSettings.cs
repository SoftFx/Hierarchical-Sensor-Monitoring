using HSMDatabase.AccessManager.DatabaseEntities;
using System.Linq;

namespace HSMServer.Core.Model
{
    public sealed class ProductNotificationSettings : NotificationSettings
    {
        internal ProductNotificationSettings() : base() { }

        internal ProductNotificationSettings(ProductNotificationSettingsEntity entity) : base(entity) { }


        internal ProductNotificationSettingsEntity ToEntity() =>
            new()
            {
                TelegramSettings = Telegram.ToEntity(),
                IgnoredSensors = IgnoredSensors.ToDictionary(s => s.Key.ToString(), s => s.Value.Ticks),
            };
    }
}
