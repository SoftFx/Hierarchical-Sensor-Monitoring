using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Model.TreeViewModel;
using HSMServer.ServerConfiguration;
using System;
using System.Threading.Tasks;

namespace HSMServer.Notifications
{
    public sealed class NotificationsCenter : IAsyncDisposable
    {
        public TelegramBot TelegramBot { get; }


        public NotificationsCenter(IUserManager userManager, TreeViewModel tree, ITreeValuesCache cache, IServerConfig config)
        {
            TelegramBot = new(userManager, cache, tree, config.Telegram);
        }


        public Task Start() => TelegramBot.StartBot();

        public ValueTask DisposeAsync() => TelegramBot.DisposeAsync();


        internal void CheckNotificationCenterState()
        {
            TelegramBot.RemoveOldInvitationTokens();
        }
    }
}
