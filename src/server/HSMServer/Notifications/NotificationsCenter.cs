using HSMServer.Authentication;
using HSMServer.Configuration;
using HSMServer.Core.Cache;
using HSMServer.Model.TreeViewModel;
using System;
using System.Threading.Tasks;

namespace HSMServer.Notifications
{
    public sealed class NotificationsCenter : IAsyncDisposable
    {
        public TelegramBot TelegramBot { get; }


        public NotificationsCenter(IUserManager userManager, TreeViewModel tree, ITreeValuesCache cache, IConfigurationProvider config)
        {
            TelegramBot = new(userManager, cache, tree, config);

            _ = TelegramBot.StartBot();
        }


        public ValueTask DisposeAsync() => TelegramBot.DisposeAsync();

        internal void CheckNotificationCenterState()
        {
            TelegramBot.RemoveOldInvitationTokens();
        }
    }
}
