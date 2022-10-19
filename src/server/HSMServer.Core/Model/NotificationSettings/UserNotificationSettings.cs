using HSMDatabase.AccessManager.DatabaseEntities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model
{
    public sealed class UserNotificationSettings : NotificationSettings
    {
        public HashSet<Guid> EnabledSensors { get; } = new();

        public ConcurrentDictionary<Guid, DateTime> IgnoredSensors { get; } = new();


        public UserNotificationSettings() : base() { }

        internal UserNotificationSettings(UserNotificationSettingsEntity entity) : base(entity)
        {
            if (entity?.EnabledSensors is not null)
            {
                EnabledSensors.Clear();

                foreach (var sensorIdStr in entity.EnabledSensors)
                    if (Guid.TryParse(sensorIdStr, out var sensorId))
                        EnabledSensors.Add(sensorId);
            }

            if (entity?.IgnoredSensors is not null)
            {
                IgnoredSensors.Clear();

                foreach (var (sensorIdStr, endIgnorePeriodTicks) in entity.IgnoredSensors)
                    if (Guid.TryParse(sensorIdStr, out var sensorId))
                        IgnoredSensors.TryAdd(sensorId, new DateTime(endIgnorePeriodTicks));
            }
        }


        public bool IsSensorEnabled(Guid sensorId) => EnabledSensors.Contains(sensorId);

        public bool IsSensorIgnored(Guid sensorId) => IgnoredSensors.ContainsKey(sensorId);

        public bool RemoveSensor(Guid sensorId)
        {
            bool isSensorRemoved = false;

            isSensorRemoved |= EnabledSensors.Remove(sensorId);
            isSensorRemoved |= IgnoredSensors.TryRemove(sensorId, out _);

            return isSensorRemoved;
        }

        internal new UserNotificationSettingsEntity ToEntity() =>
            new()
            {
                TelegramSettings = Telegram.ToEntity(),
                EnabledSensors = EnabledSensors.Select(s => s.ToString()).ToList(),
                IgnoredSensors = IgnoredSensors.ToDictionary(s => s.Key.ToString(), s => s.Value.Ticks),
            };
    }
}
