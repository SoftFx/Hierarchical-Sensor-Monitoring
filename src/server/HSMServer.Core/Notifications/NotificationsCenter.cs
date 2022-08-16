using HSMServer.Core.Authentication;
using HSMServer.Core.Configuration;
using System;
using System.Threading.Tasks;

namespace HSMServer.Core.Notifications
{
    public sealed class NotificationsCenter : INotificationsCenter, IAsyncDisposable
    {
        public TelegramBot TelegramBot { get; }


        public NotificationsCenter(IConfigurationProvider config, IUserManager userManager)
        {
            TelegramBot = new(userManager, config);

            TelegramBot.StartBot(); // TODO: start bot on button click
        }


        public async ValueTask DisposeAsync()
        {
            await TelegramBot.DisposeAsync();
        }
    }
}
