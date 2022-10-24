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


        public bool WhetherSendMessage(BaseSensorModel sensor, ValidationResult oldStatus)
        {
            var newStatus = sensor.ValidationResult;
            var minStatus = Notifications.Telegram.MessagesMinStatus;

            return AreNotificationsEnabled(sensor) &&
                   newStatus != oldStatus &&
                   (newStatus.Result >= minStatus || oldStatus.Result >= minStatus);
        }

        internal bool AreNotificationsEnabled(BaseSensorModel sensor);
    }
}
