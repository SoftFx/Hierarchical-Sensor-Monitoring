using HSMDatabase.AccessManager.DatabaseEntities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Telegram.Bot.Types;

namespace HSMServer.Notification.Settings
{
    public class ClientNotifications : NotificationSettings
    {
        public ConcurrentDictionary<ChatId, ConcurrentDictionary<Guid, DateTime>> PartiallyIgnored { get; } = new();

        public HashSet<Guid> EnabledSensors { get; } = new();


        [Obsolete("Remove after migration IgnoredSensors->PartiallyIgnored")]
        public bool Migrated { get; }


        public ClientNotifications() : base() { }

        internal ClientNotifications(NotificationSettingsEntity entity, Func<NotificationSettings> getParent = null) : base(entity, getParent)
        {
            if (entity?.EnabledSensors is not null)
            {
                EnabledSensors.Clear();

                foreach (var sensorIdStr in entity.EnabledSensors)
                    if (Guid.TryParse(sensorIdStr, out var sensorId))
                        EnabledSensors.Add(sensorId);
            }

            if (entity?.PartiallyIgnored is not null)
            {
                PartiallyIgnored.Clear();

                foreach (var (chat, sensors) in entity.PartiallyIgnored)
                {
                    var ignoredSensors = new ConcurrentDictionary<Guid, DateTime>();
                    foreach (var (sensorIdStr, endIgnorePeriodTicks) in sensors)
                        if (Guid.TryParse(sensorIdStr, out var sensorId))
                            ignoredSensors.TryAdd(sensorId, new DateTime(endIgnorePeriodTicks));

                    PartiallyIgnored.TryAdd(new(chat), ignoredSensors);
                }
            }
            else if (entity?.IgnoredSensors is not null) // TODO: remove migration
            {
                if (!Telegram.Chats.IsEmpty)
                    foreach (var (chat, _) in Telegram.Chats)
                    {
                        var ignoredSensors = new ConcurrentDictionary<Guid, DateTime>();
                        foreach (var (sensorIdStr, endIgnorePeriodTicks) in entity.IgnoredSensors)
                            if (Guid.TryParse(sensorIdStr, out var sensorId))
                                ignoredSensors.TryAdd(sensorId, new DateTime(endIgnorePeriodTicks));

                        PartiallyIgnored.TryAdd(chat, ignoredSensors);
                    }

                Migrated = true;
            }
        }


        public bool IsSensorIgnored(Guid sensorId, ChatId chatId = null)
        {
            return chatId is null
                ? PartiallyIgnored.Any(ch => ch.Value.ContainsKey(sensorId))
                : PartiallyIgnored.TryGetValue(chatId, out var ignoredSensors) && ignoredSensors.ContainsKey(sensorId);
        }

        public bool IsSensorEnabled(Guid sensorId) => EnabledSensors.Contains(sensorId);

        public bool RemoveSensor(Guid sensorId)
        {
            bool isSensorRemoved = EnabledSensors.Remove(sensorId);

            foreach (var (_, ignoredSensors) in PartiallyIgnored)
                isSensorRemoved |= ignoredSensors.TryRemove(sensorId, out _);

            return isSensorRemoved;
        }


        public void Enable(Guid sensorId, ChatId chatId = null)
        {
            if (chatId is not null && !Telegram.Chats.ContainsKey(chatId))
                return;

            var enabled = EnabledSensors.Add(sensorId);

            foreach (var (chat, ignoredSensors) in PartiallyIgnored)
            {
                if (chatId is null || chat == chatId)
                    ignoredSensors.TryRemove(sensorId, out _);
                else if (enabled)
                    ignoredSensors.TryAdd(sensorId, DateTime.MaxValue);
            }
        }

        public void Ignore(Guid sensorId, DateTime endOfIgnorePeriod, ChatId chatId = null)
        {
            if (IsSensorEnabled(sensorId))
            {
                if (chatId is null)
                    foreach (var (_, ignoredSensors) in PartiallyIgnored)
                        ignoredSensors.TryAdd(sensorId, endOfIgnorePeriod);
                else if (PartiallyIgnored.TryGetValue(chatId, out var ignoredSensors))
                    ignoredSensors.TryAdd(sensorId, endOfIgnorePeriod);

                if (PartiallyIgnored.Values.All(s => s.ContainsKey(sensorId)))
                    EnabledSensors.Remove(sensorId);
            }
        }

        public void RemoveIgnore(Guid sensorId, ChatId chatId = null)
        {
            if (IsSensorIgnored(sensorId, chatId))
            {
                if (chatId is null)
                    foreach (var (_, ignoredSensors) in PartiallyIgnored)
                        ignoredSensors.TryRemove(sensorId, out _);
                else if (PartiallyIgnored.TryGetValue(chatId, out var ignoredSensors))
                    ignoredSensors.TryRemove(sensorId, out _);

                EnabledSensors.Add(sensorId);
            }
        }

        public new NotificationSettingsEntity ToEntity() => new()
        {
            AutoSubscription = AutoSubscription,
            TelegramSettings = Telegram.ToEntity(),
            EnabledSensors = EnabledSensors.Select(s => s.ToString()).ToList(),
            PartiallyIgnored = PartiallyIgnored.ToDictionary(s => s.Key.Identifier ?? 0L, s => s.Value.ToDictionary(i => i.Key.ToString(), i => i.Value.Ticks)),
        };
    }
}
