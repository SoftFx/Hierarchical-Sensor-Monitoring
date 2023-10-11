using HSMServer.Core.Cache;
using HSMServer.Folders;
using HSMServer.ServerConfiguration;
using System;
using System.Threading.Tasks;

namespace HSMServer.Notifications
{
    public sealed class NotificationsCenter : IAsyncDisposable
    {
        public TelegramBot TelegramBot { get; }


        public NotificationsCenter(ITelegramChatsManager telegramChats, IFolderManager folderManager, ITreeValuesCache cache, IServerConfig config)
        {
            TelegramBot = new(telegramChats, folderManager, cache, config.Telegram);
        }


        public Task Start() => TelegramBot.StartBot();

        public ValueTask DisposeAsync() => TelegramBot.DisposeAsync();


        internal void CheckState()
        {
            TelegramBot.RemoveOldInvitationTokens();
        }
    }
}
