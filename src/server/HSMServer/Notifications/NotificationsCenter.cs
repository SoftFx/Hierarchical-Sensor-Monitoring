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


        public Task StartAsync() => TelegramBot.StartBotAsync();

        public ValueTask DisposeAsync()
        {
            _telegramChatsManager.ConnectChatToFolder -= _folderManager.AddChatToFolder;
            _telegramChatsManager.Removed -= _folderManager.RemoveChatHandler;

            _folderManager.RemoveFolderFromChats -= _telegramChatsManager.RemoveFolderFromChats;
            _folderManager.AddFolderToChats -= _telegramChatsManager.AddFolderToChats;
            _folderManager.Removed -= _telegramChatsManager.RemoveFolderHandler;

            return TelegramBot.DisposeAsync();
        }


        internal ValueTask SendAllMessagesAsync()
        {
            return TelegramBot.SendMessagesAsync();
        }

        internal Task RecalculateState()
        {
            _telegramChatsManager.TokenManager.RemoveOldTokens();

            return TelegramBot.ChatNamesSynchronization();
        }

        private void ConnectFoldersAndChats()
        {
            _telegramChatsManager.ConnectChatToFolder += _folderManager.AddChatToFolder;
            _telegramChatsManager.Removed += _folderManager.RemoveChatHandler;

            _folderManager.RemoveFolderFromChats += _telegramChatsManager.RemoveFolderFromChats;
            _folderManager.AddFolderToChats += _telegramChatsManager.AddFolderToChats;
            _folderManager.Removed += _telegramChatsManager.RemoveFolderHandler;
            _folderManager.GetChatName += _telegramChatsManager.GetChatName;

            foreach (var folder in _folderManager.GetValues())
                foreach (var chatId in folder.TelegramChats)
                    if (_telegramChatsManager.TryGetValue(chatId, out var chat))
                        chat.Folders.Add(folder.Id);
        }
    }
}
