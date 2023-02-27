﻿using System;
using System.Collections.Concurrent;

namespace HSMServer.Core.Model
{
    public interface INotificatable
    {
        public Guid Id { get; }

        public string Name { get; }

        public NotificationSettings Notifications { get; }

        public ConcurrentDictionary<Telegram.Bot.Types.ChatId, TelegramChat> Chats =>
            Notifications?.Telegram.Chats ?? new();


        public bool NotificationsEnabled(BaseSensorModel sensor) =>
            Notifications.Telegram.MessagesAreEnabled &&
            Notifications.IsSensorEnabled(sensor.Id) &&
            !Notifications.IsSensorIgnored(sensor.Id);
    }
}
