using HSMServer.Core.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Configuration;
using System;
using System.Threading.Tasks;

namespace HSMServer.Notifications
{
    public sealed class NotificationsCenter : INotificationsCenter, IAsyncDisposable
    {
        public TelegramBot TelegramBot { get; }


        public NotificationsCenter(IUserManager userManager, ITreeValuesCache cache, IConfigurationProvider config)
        {
            TelegramBot = new(userManager, cache, config);
            TelegramBot.StartBot();
        }

        public async ValueTask DisposeAsync()
        {
            await TelegramBot.DisposeAsync();
        }
    }
}
