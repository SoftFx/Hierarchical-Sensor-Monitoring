using HSMServer.Notifications;
using System;
using System.Collections.Concurrent;
using Telegram.Bot.Types;

namespace HSMServer.Notification.Settings
{
    public interface INotificatable
    {
        public Guid Id { get; }

        public string Name { get; }

        public ClientNotifications Notifications { get; }

        public ConcurrentDictionary<ChatId, TelegramChat> Chats =>
            Notifications?.Telegram.Chats ?? new();
    }
}
