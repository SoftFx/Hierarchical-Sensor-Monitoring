using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Folders;
using HSMServer.Model.TreeViewModel;
using HSMServer.ServerConfiguration;
using System;
using System.Threading.Tasks;

namespace HSMServer.Notifications
{
    public sealed class NotificationsCenter : IAsyncDisposable
    {
        public TelegramBot TelegramBot { get; }


        public NotificationsCenter(ITelegramChatsManager telegramChats, IFolderManager folderManager, IUserManager userManager, TreeViewModel tree, ITreeValuesCache cache, IServerConfig config)
        {
            TelegramBot = new(telegramChats, folderManager, userManager, cache, tree, config.Telegram);
        }


        public Task Start() => TelegramBot.StartBot();

        public ValueTask DisposeAsync() => TelegramBot.DisposeAsync();


        internal void CheckState()
        {
            TelegramBot.RemoveOldInvitationTokens();
        }
    }
}
