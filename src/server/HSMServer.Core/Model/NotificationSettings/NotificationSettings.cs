using HSMDatabase.AccessManager.DatabaseEntities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model
{
    public sealed class NotificationSettings
    {
        public TelegramSettings Telegram { get; }

        public HashSet<Guid> EnabledSensors { get; } = new();


        internal NotificationSettings()
        {
            Telegram = new();
        }

        internal NotificationSettings(NotificationSettingsEntity entity)
        {
            Telegram = new(entity?.TelegramSettings);

            if (entity?.EnabledSensors is not null)
                EnabledSensors = new(entity.EnabledSensors.Select(s => Guid.Parse(s)));
        }


        internal NotificationSettingsEntity ToEntity() =>
            new()
            {
                TelegramSettings = Telegram.ToEntity(),
                EnabledSensors = EnabledSensors.Select(s => s.ToString()).ToList(),
            };
    }
}
