using HSMServer.Core.Model;
using HSMServer.Notifications.Telegram;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace HSMServer.Notification.Settings
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


    internal sealed class NotificatableComparator : IEqualityComparer<INotificatable>
    {
        public bool Equals(INotificatable x, INotificatable y) => x.Id == y.Id;

        public int GetHashCode([DisallowNull] INotificatable obj) => obj.Id.GetHashCode();
    }
}
