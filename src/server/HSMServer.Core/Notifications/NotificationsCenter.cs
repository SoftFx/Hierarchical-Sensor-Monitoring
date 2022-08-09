using HSMServer.Core.Configuration;
using System;
using System.Threading.Tasks;

namespace HSMServer.Core.Notifications
{
    public sealed class NotificationsCenter : INotificationsCenter, IAsyncDisposable
    {
        public TelegramBot TelegramBot { get; }


        public NotificationsCenter(IConfigurationProvider config)
        {
            TelegramBot = new();
        }


        public async ValueTask DisposeAsync()
        {
            await TelegramBot.DisposeAsync();
        }
    }
}
