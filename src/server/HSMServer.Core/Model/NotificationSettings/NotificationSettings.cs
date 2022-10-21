using HSMDatabase.AccessManager.DatabaseEntities;
using System;
using System.Collections.Concurrent;

namespace HSMServer.Core.Model
{
    public abstract class NotificationSettings
    {
        public TelegramSettings Telegram { get; }

        public ConcurrentDictionary<Guid, DateTime> IgnoredSensors { get; } = new();


        internal NotificationSettings()
        {
            Telegram = new();
        }

        internal NotificationSettings(NotificationSettingsEntity entity)
        {
            Telegram = new(entity?.TelegramSettings);

            if (entity?.IgnoredSensors is not null)
            {
                IgnoredSensors.Clear();

                foreach (var (sensorIdStr, endIgnorePeriodTicks) in entity.IgnoredSensors)
                    if (Guid.TryParse(sensorIdStr, out var sensorId))
                        IgnoredSensors.TryAdd(sensorId, new DateTime(endIgnorePeriodTicks));
            }
        }


        public bool IsSensorIgnored(Guid sensorId) => IgnoredSensors.ContainsKey(sensorId);

        public bool RemoveSensor(Guid sensorId)
        {
            bool isSensorRemoved = false;

            isSensorRemoved |= RemoveSensorInternal(sensorId);
            isSensorRemoved |= IgnoredSensors.TryRemove(sensorId, out _);

            return isSensorRemoved;
        }

        protected virtual bool RemoveSensorInternal(Guid sensorId) => false;
    }
}
