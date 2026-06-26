using HSMServer.Core.Managers;
using HSMServer.Core.Notifications;
using System.Threading.Tasks;

namespace HSMServer.Notifications.Channels
{
    internal sealed class TelegramNotificationChannel : INotificationChannel
    {
        private readonly TelegramBot _bot;

        public NotificationKind Kind => NotificationKind.Telegram;

        internal TelegramNotificationChannel(TelegramBot bot) => _bot = bot;

        public Task DeliverAsync(AlertMessage message) => _bot.DeliverAsync(message);

        public Task FlushAsync() => _bot.SendMessagesAsync().AsTask();
    }
}
