using HSMServer.Core.Cache;
using HSMServer.Core.Managers;
using HSMServer.Folders;
using HSMServer.Notifications.Telegram.AddressBook;
using HSMServer.ServerConfiguration;
using System;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;

namespace HSMServer.Notifications
{
    public sealed class TelegramBot : IAsyncDisposable
    {
        private const string ConfigurationsError = "Invalid Bot configurations.";
        public const string BotIsNotRunningError = "Telegram Bot is not running.";

        private readonly NLog.Logger _logger = NLog.LogManager.GetCurrentClassLogger();

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


        private string BotToken => _config.BotToken;

        private bool CanSendNotifications => IsBotRunning && _config.IsRunning;


        public bool IsBotRunning => _bot is not null;


        internal TelegramBot(ITelegramChatsManager chatsManager, IFolderManager folderManager, ITreeValuesCache cache, TelegramConfig config)
        {
            _folderManager = folderManager;
            _chatsManager = chatsManager;

            _config = config;
            _cache = cache;

            _cache.NewAlertMessageEvent += StoreMessage;

            _updateHandler = new(_chatsManager, _cache, _folderManager, config);
        }

        public async ValueTask DisposeAsync()
        {
            await StopBot();
        }

        internal async Task<(string link, string error)> TryGetChatLink(long chatId)
        {
            if (IsBotRunning)
            {
                try
                {
                    var link = await _bot.CreateChatInviteLinkAsync(new ChatId(chatId), cancellationToken: _tokenSource.Token);

                    return (link.InviteLink, null);
                }
                catch (Exception ex)
                {
                    return (null, ex.Message);
                }
            }

            return (null, BotIsNotRunningError);
        }

        internal void SendTestMessage(ChatId chatId, string message)
        {
            if (IsBotRunning)
                _bot.SendTextMessageAsync(chatId, message, cancellationToken: _tokenSource.Token);
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
                System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;

                await _bot.GetMeAsync(_tokenSource.Token);
            }
            catch (Exception exc)
            {
                _bot = null;

                _logger.Error($"Invalid credentials: {BotToken}, name = {_config?.BotName}");
                _logger.Error(exc);

                return $"An error ({exc.Message}) has been occurred while starting the Bot. Please check Bot configurations. The current state of the Bot is stopped.";
            }

            await _bot.SetMyCommandsAsync(TelegramBotCommands.Commands, cancellationToken: _tokenSource.Token);

            await ChatNamesSynchronization();

            _bot.StartReceiving(_updateHandler, _options, _tokenSource.Token);

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
                    await bot.DeleteWebhookAsync();
                    await bot.CloseAsync();
                }
                catch (Exception ex)
                {
                    return $"An error ({ex.Message}) has been occurred while stopping the Bot. The current state of the Bot is stopped. Try to restart Bot again.";
                }
            }

            return string.Empty;
        }

        private void StoreMessage(AlertMessage message)
        {
            try
            {
                if (CanSendNotifications && _folderManager.TryGetValue(message.FolderId, out var folder))
                    foreach (var alert in message.Alerts)
                    {
                        var chatIds = alert.Destination.AllChats ? folder.TelegramChats : alert.Destination.Chats;

                        foreach (var chatId in chatIds)
                            if (_chatsManager.TryGetValue(chatId, out var chat) && chat.SendMessages)
                            {
                                IMessageBuilder builder = message is ScheduleAlertMessage ? chat.ScheduleMessageBuilder : chat.MessageBuilder;

                                if (chat.MessagesAggregationTimeSec == 0)
                                    SendMessage(chat.ChatId, alert.ToString());
                                else
                                    builder.AddMessage(alert);
                            }
                    }
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
            }
        }

        internal void SendMessages()
        {
            if (CanSendNotifications)
            {
                try
                {
                    foreach (var chat in _chatsManager.GetValues())
                    {
                        try
                        {
                            if (chat.ShouldSendNotification)
                            {
                                foreach (var notification in chat.GetNotifications())
                                {
                                    if (_tokenSource.IsCancellationRequested)
                                        break;
                                    else
                                        SendMessage(chat.ChatId, notification);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Error($"Error getting message: {chat.Name} - {ex}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex);
                }
            }
        }

        internal async Task ChatNamesSynchronization()
        {
            if (IsBotRunning)
            {
                foreach (var chat in _chatsManager.GetValues())
                {
                    try
                    {
                        var telegramChat = await _bot.GetChatAsync(chat.ChatId, _tokenSource.Token);

                        var chatName = chat.Type is ConnectedChatType.TelegramPrivate ? telegramChat.Username : telegramChat.Title;
                        var chatDescription = telegramChat.Description;

                        if (chat.Name != chatName || chat.Description != chatDescription)
                        {
                            var update = new TelegramChatUpdate()
                            {
                                Id = chat.Id,
                                Name = chatName,
                                Description = chatDescription,
                            };

                            await _chatsManager.TryUpdate(update);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Telegram chat name '{chat.Name}' updating is failed - {ex}");
                    }
                }
            }
        }

        //private void SendMarkdownMessageAsync(ChatId chat, string message) =>
        //    _bot?.SendTextMessageAsync(chat, message, ParseMode.MarkdownV2, cancellationToken: _tokenSource.Token);

        private void SendMessage(ChatId chat, string message)
        {
            if (!string.IsNullOrEmpty(message))
                _bot?.SendTextMessageAsync(chat, message, cancellationToken: _tokenSource.Token);
        }
    }
}
