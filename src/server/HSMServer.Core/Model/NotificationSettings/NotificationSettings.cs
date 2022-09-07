using HSMDatabase.AccessManager.DatabaseEntities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model
{
    public class NodeNotificationsState
    {
        public bool IsAnyEnabled { get; set; }

        public bool IsAllEnabled { get; set; }

        public bool IsAnyIgnored { get; set; }


        internal void Reset()
        {
            IsAnyEnabled = false;
            IsAllEnabled = true;
            IsAnyIgnored = true;
        }
    }


    public sealed class NotificationSettings
    {
        public TelegramSettings Telegram { get; }

        public HashSet<Guid> EnabledSensors { get; } = new();

        public ConcurrentDictionary<Guid, DateTime> IgnoredSensors { get; } = new();

        public ConcurrentDictionary<string, NodeNotificationsState> Nodes { get; } = new();


        internal NotificationSettings()
        {
            Telegram = new();
        }

        internal NotificationSettings(NotificationSettingsEntity entity)
        {
            Telegram = new(entity?.TelegramSettings);

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


        public bool RemoveSensor(Guid sensorId)
        {
            bool isSensorRemoved = false;

            isSensorRemoved |= EnabledSensors.Remove(sensorId);
            isSensorRemoved |= IgnoredSensors.TryRemove(sensorId, out _);

            return isSensorRemoved;
        }

        public void InitNodeNotificationsState(string nodeId)
        {
            if (!Nodes.ContainsKey(nodeId))
                Nodes.TryAdd(nodeId, new NodeNotificationsState());

            Nodes[nodeId].Reset();
        }

        internal NotificationSettingsEntity ToEntity() =>
            new()
            {
                TelegramSettings = Telegram.ToEntity(),
                EnabledSensors = EnabledSensors.Select(s => s.ToString()).ToList(),
                IgnoredSensors = IgnoredSensors.ToDictionary(s => s.Key.ToString(), s => s.Value.Ticks),
            };
    }
}
