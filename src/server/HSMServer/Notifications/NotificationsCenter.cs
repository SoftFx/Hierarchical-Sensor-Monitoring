using HSMServer.Core.Cache;
using HSMServer.Core.Managers;
using HSMServer.Folders;
using HSMServer.Notifications.Channels;
using HSMServer.Notifications.Chats;
using HSMServer.ServerConfiguration;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HSMServer.Notifications
{
    public sealed class NotificationsCenter : IAsyncDisposable
    {
        private readonly IChatsManager _chatsManager;
        private readonly IFolderManager _folderManager;
        private readonly ITreeValuesCache _cache;
        private readonly IReadOnlyList<INotificationChannel> _channels;


        public TelegramBot TelegramBot { get; }

        public SlackNotificationChannel SlackChannel { get; }

        public MattermostNotificationChannel MattermostChannel { get; }


        public NotificationsCenter(IChatsManager chats, IFolderManager folderManager, ITreeValuesCache cache, IServerConfig config,
                                   SlackNotificationChannel slackChannel, MattermostNotificationChannel mattermostChannel)
        {
            _chatsManager = chats;
            _folderManager = folderManager;
            _cache = cache;

            ConnectFoldersAndChats();

            TelegramBot = new(_chatsManager, folderManager, config.Telegram);
            SlackChannel = slackChannel;
            MattermostChannel = mattermostChannel;
            _channels = [new TelegramNotificationChannel(TelegramBot), SlackChannel, MattermostChannel];

            _cache.NewAlertMessageEvent += DispatchAlertMessage;
        }


        public Task StartAsync() => TelegramBot.StartBotAsync();

        public ValueTask DisposeAsync()
        {
            _cache.NewAlertMessageEvent -= DispatchAlertMessage;

            _chatsManager.ConnectChatToFolder -= _folderManager.AddChatToFolder;
            _chatsManager.Removed -= _folderManager.RemoveChatHandler;

            _folderManager.RemoveFolderFromChats -= _chatsManager.RemoveFolderFromChats;
            _folderManager.AddFolderToChats -= _chatsManager.AddFolderToChats;
            _folderManager.Removed -= _chatsManager.RemoveFolderHandler;
            _folderManager.GetChatName -= GetChatName;

            return TelegramBot.DisposeAsync();
        }


        internal async ValueTask SendAllMessagesAsync()
        {
            foreach (var channel in _channels)
                await channel.FlushAsync();
        }

        internal Task RecalculateState()
        {
            _chatsManager.TokenManager.RemoveOldTokens();

            return TelegramBot.ChatNamesSynchronization();
        }

        private async void DispatchAlertMessage(AlertMessage message)
        {
            foreach (var channel in _channels)
                await channel.DeliverAsync(message);
        }

        private void ConnectFoldersAndChats()
        {
            _chatsManager.ConnectChatToFolder += _folderManager.AddChatToFolder;
            _chatsManager.Removed += _folderManager.RemoveChatHandler;

            _folderManager.AddFolderToChats += _chatsManager.AddFolderToChats;
            _folderManager.RemoveFolderFromChats += _chatsManager.RemoveFolderFromChats;

            _folderManager.Removed += _chatsManager.RemoveFolderHandler;

            _folderManager.GetChatName += GetChatName;

            foreach (var folder in _folderManager.GetValues())
                foreach (var chatId in folder.Chats)
                    if (_chatsManager.TryGetValue(chatId, out var chat))
                        chat.Folders.Add(folder.Id);
        }

        private string GetChatName(Guid id) => _chatsManager.GetChatName(id);
    }
}
