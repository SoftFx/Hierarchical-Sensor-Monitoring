using HSMServer.Core.Authentication;
using HSMServer.Core.Configuration;
using System;
using System.Threading.Tasks;

namespace HSMServer.Core.Notifications
{
    public sealed class NotificationsCenter : INotificationsCenter, IAsyncDisposable
    {
        private readonly IConfigurationProvider _configurationProvider;
        private readonly IUserManager _userManager;

        public TelegramBot TelegramBot { get; private set; }


        public NotificationsCenter(IConfigurationProvider config, IUserManager userManager)
        {
            _configurationProvider = config;
            _userManager = userManager;

            StartBot();
        }

        public async void StartBot()
        {
            if (TelegramBot is not null)
                await TelegramBot.StopBot();

            TelegramBot = new(_userManager, _configurationProvider);
            TelegramBot.StartBot();
        }

        public async ValueTask DisposeAsync()
        {
            await TelegramBot.DisposeAsync();
        }
    }
}
