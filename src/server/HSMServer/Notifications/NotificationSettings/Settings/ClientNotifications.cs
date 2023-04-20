using HSMDatabase.AccessManager.DatabaseEntities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Notification.Settings
{
    public class ClientNotifications : NotificationSettings
    {
        public ConcurrentDictionary<Guid, DateTime> IgnoredSensors { get; } = new();

        public HashSet<Guid> EnabledSensors { get; } = new();


        public ClientNotifications() : base() { }

        internal ClientNotifications(NotificationSettingsEntity entity, Func<NotificationSettings> getParent = null) : base(entity, getParent)
        {
            if (entity?.IgnoredSensors is not null)
            {
                IgnoredSensors.Clear();

                foreach (var (sensorIdStr, endIgnorePeriodTicks) in entity.IgnoredSensors)
                    if (Guid.TryParse(sensorIdStr, out var sensorId))
                        IgnoredSensors.TryAdd(sensorId, new DateTime(endIgnorePeriodTicks));
            }

            if (entity?.EnabledSensors is not null)
            {
                EnabledSensors.Clear();

                foreach (var sensorIdStr in entity.EnabledSensors)
                    if (Guid.TryParse(sensorIdStr, out var sensorId))
                        EnabledSensors.Add(sensorId);
            }
        }


        public bool IsSensorIgnored(Guid sensorId) => IgnoredSensors.ContainsKey(sensorId);

        public bool IsSensorEnabled(Guid sensorId) => EnabledSensors.Contains(sensorId);

        public bool RemoveSensor(Guid sensorId)
        {
            bool isSensorRemoved = false;

            isSensorRemoved |= EnabledSensors.Remove(sensorId);
            isSensorRemoved |= IgnoredSensors.TryRemove(sensorId, out _);

            return isSensorRemoved;
        }


        public void Enable(Guid sensorId)
        {
            EnabledSensors.Add(sensorId);
            IgnoredSensors.TryRemove(sensorId, out _);
        }

        public void Ignore(Guid sensorId, DateTime endOfIgnorePeriod)
        {
            if (IsSensorEnabled(sensorId))
            {
                IgnoredSensors.TryAdd(sensorId, endOfIgnorePeriod);
                EnabledSensors.Remove(sensorId);
            }
        }

        public void RemoveIgnore(Guid sensorId)
        {
            if (IsSensorIgnored(sensorId))
            {
                IgnoredSensors.TryRemove(sensorId, out _);
                EnabledSensors.Add(sensorId);
            }
        }

        public void Disable(Guid sensorId) => RemoveSensor(sensorId);

        public new NotificationSettingsEntity ToEntity() => new()
        {
            TelegramSettings = Telegram.ToEntity(),
            EnabledSensors = EnabledSensors.Select(s => s.ToString()).ToList(),
            IgnoredSensors = IgnoredSensors.ToDictionary(s => s.Key.ToString(), s => s.Value.Ticks),
        };
    }
}
