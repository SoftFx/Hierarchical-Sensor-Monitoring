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
            _folderManager = folderManager;

            ConnectFoldersAndChats();

            TelegramBot = new(telegramChats, folderManager, cache, config.Telegram);
        }


        public Task Start() => TelegramBot.StartBot();

        public ValueTask DisposeAsync()
        {
            _telegramChatsManager.ConnectChatToFolder -= _folderManager.AddChatToFolder;
            _telegramChatsManager.Removed -= _folderManager.RemoveChatHandler;

            _folderManager.RemoveFolderFromChats -= _telegramChatsManager.RemoveFolderFromChats;
            _folderManager.AddFolderToChats -= _telegramChatsManager.AddFolderToChats;
            _folderManager.Removed -= _telegramChatsManager.RemoveFolderHandler;

            return TelegramBot.DisposeAsync();
        }


        internal void SendAllMessages()
        {
            TelegramBot.SendMessages();
        }

        internal void RecalculateState()
        {
            _telegramChatsManager.TokenManager.RemoveOldTokens();
        }

        private void ConnectFoldersAndChats()
        {
            _telegramChatsManager.ConnectChatToFolder += _folderManager.AddChatToFolder;
            _telegramChatsManager.Removed += _folderManager.RemoveChatHandler;

            _folderManager.RemoveFolderFromChats += _telegramChatsManager.RemoveFolderFromChats;
            _folderManager.AddFolderToChats += _telegramChatsManager.AddFolderToChats;
            _folderManager.Removed += _telegramChatsManager.RemoveFolderHandler;

            foreach (var folder in _folderManager.GetValues())
                foreach (var chatId in folder.TelegramChats)
                    if (_telegramChatsManager.TryGetValue(chatId, out var chat))
                        chat.Folders.Add(folder.Id);
        }
    }
}
