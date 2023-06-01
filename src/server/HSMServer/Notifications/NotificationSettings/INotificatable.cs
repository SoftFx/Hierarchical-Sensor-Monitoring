using HSMServer.Core.Model;
using HSMServer.Notifications.Telegram;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Telegram.Bot.Types;

namespace HSMServer.Notification.Settings
{
    public interface INotificatable
    {
        public Guid Id { get; }

        public string Name { get; }

        public ClientNotifications Notifications { get; }

        public ConcurrentDictionary<Telegram.Bot.Types.ChatId, TelegramChat> Chats =>
            Notifications?.Telegram.Chats ?? new();


        public bool CanSendData(BaseSensorModel sensor, ChatId chatId) =>
            Notifications.UsedTelegram.MessagesAreEnabled &&
            Notifications.IsSensorEnabled(sensor.Id) &&
            !Notifications.IsSensorIgnored(sensor.Id, chatId);
    }


    internal sealed class NotificatableComparator : IEqualityComparer<INotificatable>
    {
        public bool Equals(INotificatable x, INotificatable y) => x.Id == y.Id;

        public int GetHashCode([DisallowNull] INotificatable obj) => obj.Id.GetHashCode();
    }
}
