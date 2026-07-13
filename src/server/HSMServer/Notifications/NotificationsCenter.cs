using HSMServer.Core.Cache;
using HSMServer.Core.Managers;
using HSMServer.Folders;
using HSMServer.Notifications.Channels;
using HSMServer.ServerConfiguration;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HSMServer.Notifications
{
    public sealed class NotificationsCenter : IAsyncDisposable
    {
        private readonly ITelegramChatsManager _telegramChatsManager;
        private readonly IFolderManager _folderManager;
        private readonly ITreeValuesCache _cache;
        private readonly ISlackDestinationsManager _slackDestinationsManager;
        private readonly IReadOnlyList<INotificationChannel> _channels;


        public TelegramBot TelegramBot { get; }

        public SlackNotificationChannel SlackChannel { get; }


        public NotificationsCenter(ITelegramChatsManager telegramChats, IFolderManager folderManager, ITreeValuesCache cache, IServerConfig config,
                                   SlackNotificationChannel slackChannel, ISlackDestinationsManager slackDestinations)
        {
            _telegramChatsManager = telegramChats;
            _folderManager = folderManager;
            _cache = cache;
            _slackDestinationsManager = slackDestinations;

            ConnectFoldersAndChats();

            TelegramBot = new(telegramChats, folderManager, config.Telegram);
            SlackChannel = slackChannel;
            _channels = [new TelegramNotificationChannel(TelegramBot), SlackChannel];

            _cache.NewAlertMessageEvent += DispatchAlertMessage;
        }


        public Task StartAsync() => TelegramBot.StartBotAsync();

        public ValueTask DisposeAsync()
        {
            _cache.NewAlertMessageEvent -= DispatchAlertMessage;

            _telegramChatsManager.ConnectChatToFolder -= _folderManager.AddChatToFolder;
            _telegramChatsManager.Removed -= _folderManager.RemoveChatHandler;
            _slackDestinationsManager.Removed -= _folderManager.RemoveSlackDestinationHandler;

            _folderManager.RemoveFolderFromChats -= _telegramChatsManager.RemoveFolderFromChats;
            _folderManager.RemoveFolderFromChats -= _slackDestinationsManager.RemoveFolderFromChats;
            _folderManager.AddFolderToChats -= _telegramChatsManager.AddFolderToChats;
            _folderManager.AddFolderToChats -= _slackDestinationsManager.AddFolderToChats;
            _folderManager.Removed -= _telegramChatsManager.RemoveFolderHandler;
            _folderManager.Removed -= _slackDestinationsManager.RemoveFolderHandler;
            _folderManager.GetChatName -= GetChatNameComposite;

            return TelegramBot.DisposeAsync();
        }


        internal async ValueTask SendAllMessagesAsync()
        {
            foreach (var channel in _channels)
                await channel.FlushAsync();
        }

        internal Task RecalculateState()
        {
            _telegramChatsManager.TokenManager.RemoveOldTokens();

            return TelegramBot.ChatNamesSynchronization();
        }

        private async void DispatchAlertMessage(AlertMessage message)
        {
            foreach (var channel in _channels)
                await channel.DeliverAsync(message);
        }

        private void ConnectFoldersAndChats()
        {
            _telegramChatsManager.ConnectChatToFolder += _folderManager.AddChatToFolder;
            _telegramChatsManager.Removed += _folderManager.RemoveChatHandler;
            _slackDestinationsManager.Removed += _folderManager.RemoveSlackDestinationHandler;

            _folderManager.AddFolderToChats += _telegramChatsManager.AddFolderToChats;
            _folderManager.AddFolderToChats += _slackDestinationsManager.AddFolderToChats;
            _folderManager.RemoveFolderFromChats += _telegramChatsManager.RemoveFolderFromChats;
            _folderManager.RemoveFolderFromChats += _slackDestinationsManager.RemoveFolderFromChats;

            _folderManager.Removed += _telegramChatsManager.RemoveFolderHandler;
            _folderManager.Removed += _slackDestinationsManager.RemoveFolderHandler;

            _folderManager.GetChatName += GetChatNameComposite;

            foreach (var folder in _folderManager.GetValues())
                foreach (var chatId in folder.Chats)
                {
                    if (_telegramChatsManager.TryGetValue(chatId, out var chat))
                        chat.Folders.Add(folder.Id);
                    else if (_slackDestinationsManager.TryGetValue(chatId, out var destination))
                        destination.Folders.Add(folder.Id);
                }
        }

        private string GetChatNameComposite(Guid id) =>
            _telegramChatsManager.GetChatName(id) ?? _slackDestinationsManager.GetSlackDestinationName(id);
    }
}
