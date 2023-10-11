using HSMServer.Core.Cache;
using HSMServer.Core.Model.Policies;
using HSMServer.Folders;
using HSMServer.ServerConfiguration;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using User = HSMServer.Model.Authentication.User;

namespace HSMServer.Notifications
{
    public sealed class TelegramBot : IAsyncDisposable
    {
        private const string ConfigurationsError = "Invalid Bot configurations.";

        private readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

        private readonly AddressBook _addressBook = new();
        private readonly ReceiverOptions _options = new()
        {
            AllowedUpdates = { }, // receive all update types
        };

        private readonly TelegramUpdateHandler _updateHandler;
        private readonly ITelegramChatsManager _chatsManager;
        private readonly IFolderManager _folderManager;
        private readonly ITreeValuesCache _cache;
        private readonly TelegramConfig _config;

        private CancellationTokenSource _tokenSource = new();
        private TelegramBotClient _bot;


        internal string BotName => _config.BotName;

        private string BotToken => _config.BotToken;

        private bool IsBotRunning => _bot is not null;


        internal TelegramBot(ITelegramChatsManager chatsManager, IFolderManager folderManager, ITreeValuesCache cache, TelegramConfig config)
        {
            _folderManager = folderManager;
            _chatsManager = chatsManager;

            _config = config;
            _cache = cache;

            _cache.ThrowAlertResultsEvent += SendMessage;

            _updateHandler = new(_addressBook, _cache, _folderManager, config);

            FillAddressBook();
        }

        public async ValueTask DisposeAsync()
        {
            await StopBot();
        }

        internal string GetInvitationLink(Guid folderId, User user) =>
            $"https://t.me/{BotName}?start={_addressBook.BuildInvitationToken(folderId, user)}";

        internal string GetStartCommandForGroup(Guid folderId, User user) =>
            $"{TelegramBotCommands.Start}@{BotName} {_addressBook.BuildInvitationToken(folderId, user)}";

        internal async Task<string> GetChatLink(long chatId)
        {
            var link = await _bot.CreateChatInviteLinkAsync(new ChatId(chatId), cancellationToken: _tokenSource.Token);

            return link.InviteLink;
        }

        internal void RemoveOldInvitationTokens() => _addressBook.RemoveOldTokens();

        internal void RemoveChat(long chatId) => _addressBook.RemoveChat(chatId);

        internal void SendTestMessage(ChatId chatId, string message)
        {
            if (IsBotRunning)
                _bot?.SendTextMessageAsync(chatId, message, cancellationToken: _tokenSource.Token);
        }

        internal async Task<string> StartBot()
        {
            if (IsBotRunning)
            {
                var message = await StopBot();
                if (!string.IsNullOrEmpty(message))
                    return message;
            }

            if (!_config.IsValid)
                return ConfigurationsError;

            _tokenSource = new CancellationTokenSource();
            _bot = new TelegramBotClient(BotToken)
            {
                Timeout = new TimeSpan(0, 5, 0) // 5 minutes timeout
            };

            try
            {
                await _bot.GetMeAsync(_tokenSource.Token);
            }
            catch (Exception exc)
            {
                _bot = null;
                return $"An error ({exc.Message}) has been occurred while starting the Bot. Please check Bot configurations. The current state of the Bot is stopped.";
            }

            await _bot.SetMyCommandsAsync(TelegramBotCommands.Commands, cancellationToken: _tokenSource.Token);

            await ChatNamesSynchronization();

            _bot.StartReceiving(_updateHandler, _options, _tokenSource.Token);
            ThreadPool.QueueUserWorkItem(async _ => await MessageReceiver());

            return string.Empty;
        }

        internal async Task<string> StopBot()
        {
            _tokenSource.Cancel();

            var bot = _bot;
            _bot = null;

            if (bot is not null)
            {
                try
                {
                    await bot?.DeleteWebhookAsync();
                    await bot?.CloseAsync();
                }
                catch (Exception ex)
                {
                    return $"An error ({ex.Message}) has been occurred while stopping the Bot. The current state of the Bot is stopped. Try to restart Bot again.";
                }
            }

            return string.Empty;
        }

        // TODO: FillAddressBook should be from telegram chats manager
        private void FillAddressBook()
        {
            foreach (var chat in _chatsManager.GetValues())
                _addressBook.RegisterChat(chat);
        }

        private void SendMessage(List<AlertResult> result, Guid folderId)
        {
            try
            {
                if (IsBotRunning && _config.IsRunning && _folderManager.TryGetValue(folderId, out var folder))
                    foreach (var alert in result)
                    {
                        var chatIds = alert.Destination.AllChats ? folder.TelegramChats : alert.Destination.Chats;

                        foreach (var chatId in chatIds)
                            if (_chatsManager.TryGetValue(chatId, out var chat) && chat.SendMessages)
                            {
                                if (chat.MessagesAggregationTimeSec == 0)
                                    SendMessage(chat.ChatId, alert.ToString());
                                else
                                    chat.MessageBuilder.AddMessage(alert);
                            }
                    }
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
            }
        }

        private async Task MessageReceiver()
        {
            while (IsBotRunning)
            {
                try
                {
                    foreach (var chat in _chatsManager.GetValues())
                    {
                        try
                        {
                            var messagesDelay = chat.MessagesAggregationTimeSec;

                            if (messagesDelay > 0 && chat.MessageBuilder.ExpectedSendingTime <= DateTime.UtcNow)
                            {
                                var message = chat.MessageBuilder.GetAggregateMessage(messagesDelay);

                                SendMessage(chat.ChatId, message);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Error($"Error getting message: {chat.Name} - {ex}");
                        }
                    }

                    if (_tokenSource.IsCancellationRequested)
                        break;

                    await Task.Delay(500, _tokenSource.Token);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex);
                }
            }
        }

        //remove after telegram settings migration
        //private void SendMarkdownMessageAsync(ChatId chat, string message) =>
        //    _bot?.SendTextMessageAsync(chat, message, ParseMode.MarkdownV2, cancellationToken: _tokenSource.Token);

        private void SendMessage(ChatId chat, string message)
        {
            if (!string.IsNullOrEmpty(message))
                _bot?.SendTextMessageAsync(chat, message, cancellationToken: _tokenSource.Token);
        }

        private async Task ChatNamesSynchronization()
        {
            foreach (var chat in _chatsManager.GetValues())
            {
                try
                {
                    var telegramChat = await _bot?.GetChatAsync(chat.ChatId, _tokenSource.Token);
                    var chatName = chat.Type is ConnectedChatType.TelegramPrivate ? telegramChat.Username : telegramChat.Title;

                    if (chat.Name != chatName)
                    {
                        chat.Name = chatName;

                        await _chatsManager.TryUpdate(chat);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"Telegram chat name '{chat.Name}' updating is failed - {ex}");
                }
            }
        }
    }
}
