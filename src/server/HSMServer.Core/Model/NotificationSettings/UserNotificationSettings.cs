using HSMDatabase.AccessManager.DatabaseEntities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model
{
    public sealed class UserNotificationSettings : NotificationSettings
    {
        public HashSet<Guid> EnabledSensors { get; } = new();


        public UserNotificationSettings() : base() { }

        public UserNotificationSettings(UserNotificationSettingsEntity entity) : base(entity)
        {
            if (entity?.EnabledSensors is not null)
            {
                EnabledSensors.Clear();

                foreach (var sensorIdStr in entity.EnabledSensors)
                    if (Guid.TryParse(sensorIdStr, out var sensorId))
                        EnabledSensors.Add(sensorId);
            }
        }


        public bool IsSensorEnabled(Guid sensorId) => EnabledSensors.Contains(sensorId);

        public UserNotificationSettingsEntity ToEntity() =>
            new()
            {
                TelegramSettings = Telegram.ToEntity(),
                EnabledSensors = EnabledSensors.Select(s => s.ToString()).ToList(),
                IgnoredSensors = IgnoredSensors.ToDictionary(s => s.Key.ToString(), s => s.Value.Ticks),
            };

        protected override bool RemoveSensorInternal(Guid sensorId) => EnabledSensors.Remove(sensorId);
    }
}
