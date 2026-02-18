using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Exceptions;
using HSMServer.Core.Cache;
using HSMServer.Core.Managers;
using HSMServer.Core.TableOfChanges;
using HSMServer.ConcurrentStorage;
using HSMServer.Folders;
using HSMServer.Notifications.Telegram.AddressBook;
using HSMServer.ServerConfiguration;
using Telegram.Bot.Types.Enums;
using HSMServer.Helpers;


namespace HSMServer.Notifications
{
    public sealed class TelegramBot : IAsyncDisposable
    {
        private readonly TimeSpan SendMessageRetryTimeout = TimeSpan.FromMinutes(1);
        private const int SendMessageRetryCount = 10;
        private const string ConfigurationsError = "Invalid Bot configurations.";
        private const string TrimmedMessage = " [this message was trimmed]";
        private const int MaxMessageLength = 1000;
        public const string BotIsNotRunningError = "Telegram Bot is not running.";

        private readonly static NLog.Logger _logger = NLog.LogManager.GetLogger(nameof(TelegramBot));

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

        public event Action<string> ErrorHandled;
        public event Action<string, string> MessageSended;
        public event Action MessageSending;

        internal TelegramBot(ITelegramChatsManager chatsManager, IFolderManager folderManager, ITreeValuesCache cache, TelegramConfig config)
        {
            _folderManager = folderManager;
            _chatsManager = chatsManager;

            _config = config;
            _cache = cache;

            _cache.NewAlertMessageEvent += StoreMessage;

            _updateHandler = new(this, _chatsManager, _folderManager, config);
        }

        public async ValueTask DisposeAsync()
        {
            await StopBotAsync();
        }

        internal async Task<(string link, string error)> TryGetChatLink(long chatId)
        {
            if (IsBotRunning)
            {
                try
                {
                    var link = await _bot.CreateChatInviteLink(new ChatId(chatId), cancellationToken: _tokenSource.Token);

                    return (link.InviteLink, null);
                }
                catch (Exception ex)
                {
                    return (null, ex.Message);
                }
            }

            return (null, BotIsNotRunningError);
        }

        internal async ValueTask SendTestMessageAsync(ChatId chatId, string message)
        {
            //if (IsBotRunning)
            //    _bot.SendMessage(chatId, message, cancellationToken: _tokenSource.Token);

            var chat = _chatsManager.GetChatByChatId(chatId);

            if (chat != null)
                await SendMessageAsync(chat, message);
        }

        internal async Task<string> StartBotAsync()
        {
            if (IsBotRunning)
            {
                var message = await StopBotAsync();
                if (!string.IsNullOrEmpty(message))
                    return message;
            }

            if (!_config.IsValid)
                return ConfigurationsError;

            HttpClientHandler clientHandler = new() //todo: should be removed after live ubuntu update
            {
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true,
                SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
            };

            _tokenSource = new CancellationTokenSource();
            _bot = new TelegramBotClient(BotToken, new HttpClient(clientHandler))
            {
                Timeout = new TimeSpan(0, 5, 0) // 5 minutes timeout
            };

            try
            {
                await _bot.GetMe(_tokenSource.Token);
            }
            catch (Exception exc)
            {
                _bot = null;

                _logger.Error($"Invalid credentials: {BotToken}, name = {_config?.BotName}");
                _logger.Error(exc);

                return $"An error ({exc.Message}) has been occurred while starting the Bot. Please check Bot configurations. The current state of the Bot is stopped.";
            }

            await _bot.SetMyCommands(TelegramBotCommands.Commands, cancellationToken: _tokenSource.Token);

            await ChatNamesSynchronization();

            _bot.StartReceiving(_updateHandler, _options, _tokenSource.Token);

            return string.Empty;
        }

        internal async Task<string> StopBotAsync()
        {
            await _tokenSource.CancelAsync();

            var bot = _bot;
            _bot = null;

            if (bot is not null)
            {
                try
                {
                    await bot.DeleteWebhook();
                    await bot.Close();
                }
                catch (Exception ex)
                {
                    return $"An error ({ex.Message}) has been occurred while stopping the Bot. The current state of the Bot is stopped. Try to restart Bot again.";
                }
            }

            return string.Empty;
        }

        private async void StoreMessage(AlertMessage message)
        {
            try
            {
                _logger.Info($"TSend: StoreMessage enter");

                if (!CanSendNotifications || !_folderManager.TryGetValue(message.FolderId, out var _))
                {
                    _logger.Info($"TSend: StoreMessage can't send: CanSendNotifications={CanSendNotifications}");
                    return;
                }

                foreach (var alert in message)
                {
                    var chatIds = alert.Destination.Chats;

                    foreach (var chatId in chatIds)
                        if (_chatsManager.TryGetValue(chatId, out var chat) && chat.SendMessages)
                        {
                            var alertText = alert.ToString();
                            var logAlert = alertText.Length > 100 ? alertText.Substring(0, 100) : alertText;

                            if (chat.MessagesAggregationTimeSec == 0)
                            {
                                await SendMessageAsync(chat, alertText);
                                _logger.Info($"TSend: SendMessageAsync '{logAlert}'");
                            }
                            else
                            {
                                IMessageBuilder builder = message is ScheduleAlertMessage ? chat.ScheduleMessageBuilder : chat.MessageBuilder;
                                builder.AddMessage(alert);

                                _logger.Info($"TSend: builder '{logAlert}'");
                            }
                        }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Send telegram: StoreMessage error: {ex}");
            }

        }

        internal async ValueTask SendMessagesAsync()
        {
            if (!CanSendNotifications)
                return;

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

                                await SendMessageAsync(chat, notification);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"SendMessagesAsync: error getting message: {chat.Name} - {ex}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
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
                        ChatFullInfo telegramChat = await _bot.GetChat(chat.ChatId, _tokenSource.Token);
        
                        if (ShouldGroupBeDeleted(telegramChat))
                        {
                            if (await _chatsManager.TryRemove(new RemoveRequest(chat.Id, InitiatorInfo.System)))
                                continue;
                        }
                        
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

        private static bool ShouldGroupBeDeleted(ChatFullInfo telegramChat)
        {
            if (telegramChat is null)
                return true;
            
            return telegramChat.Type switch
            {
                ChatType.Private => false,
                ChatType.Group => HasNoPermissions(telegramChat.Permissions),
                _ => false
            };

            static bool HasNoPermissions(ChatPermissions permissions)
            {
                if (permissions is null) 
                    return true;

                return !permissions.CanSendMessages && !permissions.CanSendOtherMessages;
            }
        }

        //private void SendMarkdownMessageAsync(ChatId chat, string message) =>
        //    _bot?.SendTextMessageAsync(chat, message, ParseMode.MarkdownV2, cancellationToken: _tokenSource.Token);

        private async ValueTask SendMessageAsync(TelegramChat chat, string message)
        {
            if (string.IsNullOrEmpty(message))
                return;

            int retry = 1;

            if (message.Length >= MaxMessageLength)
                message = message[..(MaxMessageLength - TrimmedMessage.Length)] + TrimmedMessage;

            while (true)
            {
                try
                {
                    if (_tokenSource.IsCancellationRequested)
                        break;

                    if (retry >= SendMessageRetryCount)
                        break;

                    MessageSending?.Invoke();

                    await (_bot?.SendMessage(chat.ChatId, MarkdownHelper.ConvertToMarkdownV2(message), cancellationToken: _tokenSource.Token, parseMode: ParseMode.MarkdownV2) ?? Task.CompletedTask);

                    var logMessage = message.Length > 100 ? message.Substring(0, 100) : message;

                    MessageSended?.Invoke(chat.Name ,message);

                    _logger.Info($"TSend: SendMessageAsync: message '{logMessage}' is sent");
                    break;
                }
                catch (ApiRequestException ex)
                {
                    OnErrorHandled($"Send telegram: An error ({ex.Message}) has been occurred while sending message #{retry} [{chat.Name}] {message}");
                    break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    OnErrorHandled($"Send telegram: An error ({ex.Message}) has been occurred while sending message #{retry} [{chat.Name}] {message}");

                    await Task.Delay(SendMessageRetryTimeout * retry, _tokenSource.Token);
                    retry++;
                }
            }
        }

        internal void OnErrorHandled(string message)
        {
            _logger.Error(message);
            ErrorHandled?.Invoke(message);
        }
    }
}
