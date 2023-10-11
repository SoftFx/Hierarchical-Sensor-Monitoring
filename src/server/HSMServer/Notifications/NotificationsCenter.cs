using HSMServer.Core.Cache;
using HSMServer.Folders;
using HSMServer.ServerConfiguration;
using System;
using System.Threading.Tasks;

namespace HSMServer.Notifications
{
    public sealed class NotificationsCenter : IAsyncDisposable
    {
        private readonly ITelegramChatsManager _telegramChatsManager;
        private readonly IFolderManager _folderManager;


        public TelegramBot TelegramBot { get; }


        public NotificationsCenter(ITelegramChatsManager telegramChats, IFolderManager folderManager, ITreeValuesCache cache, IServerConfig config)
        {
            _telegramChatsManager = telegramChats;
            _telegramChatsManager.ConnectChatToFolder += folderManager.AddChatToFolder;

            TelegramBot = new(telegramChats, folderManager, cache, config.Telegram);
        }


        public Task Start() => TelegramBot.StartBot();

        public ValueTask DisposeAsync()
        {
            _telegramChatsManager.ConnectChatToFolder -= _folderManager.AddChatToFolder;

            return TelegramBot.DisposeAsync();
        }


        internal void CheckState()
        {
            _telegramChatsManager.TokenManager.RemoveOldTokens();
        }
    }
}
