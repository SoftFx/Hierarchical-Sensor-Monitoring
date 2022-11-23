using System.Collections.Concurrent;

namespace HSMServer.Core.Model
{
    public interface INotificatable
    {
        public string Id { get; }

        public string Name { get; }

        public NotificationSettings Notifications { get; }

        public ConcurrentDictionary<Telegram.Bot.Types.ChatId, TelegramChat> Chats =>
            Notifications?.Telegram.Chats ?? new();


        public bool AreNotificationsEnabled(BaseSensorModel sensor);
    }
}
