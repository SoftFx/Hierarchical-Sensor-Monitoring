using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HSMServer.ConcurrentStorage;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Exceptions;
using HSMServer.Core.Cache;
using HSMServer.Core.Managers;
using HSMServer.Core.TableOfChanges;
using HSMServer.Folders;
using HSMServer.Notifications.Telegram.AddressBook;
using HSMServer.ServerConfiguration;
using Telegram.Bot.Types.Enums;


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
            await StopBotAsync().ConfigureAwait(false);
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
            //if (IsBotRunning)
            //    _bot.SendMessage(chatId, message, cancellationToken: _tokenSource.Token);

            SendMessageAsync(chatId, message).ConfigureAwait(false);
        }

        internal async Task<string> StartBotAsync()
        {
            if (IsBotRunning)
            {
                var message = await StopBotAsync().ConfigureAwait(false);
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
                await _bot.GetMe(_tokenSource.Token).ConfigureAwait(false);
            }
            catch (Exception exc)
            {
                _bot = null;

                _logger.Error($"Invalid credentials: {BotToken}, name = {_config?.BotName}");
                _logger.Error(exc);

                return $"An error ({exc.Message}) has been occurred while starting the Bot. Please check Bot configurations. The current state of the Bot is stopped.";
            }

            await _bot.SetMyCommands(TelegramBotCommands.Commands, cancellationToken: _tokenSource.Token).ConfigureAwait(false);

            await ChatNamesSynchronization().ConfigureAwait(false);

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
                    await bot.DeleteWebhook().ConfigureAwait(false);
                    await bot.Close().ConfigureAwait(false);
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
                if (!CanSendNotifications || !_folderManager.TryGetValue(message.FolderId, out var _))
                    return;

                foreach (var alert in message)
                {
                    var chatIds = alert.Destination.Chats;

                    foreach (var chatId in chatIds)
                        if (_chatsManager.TryGetValue(chatId, out var chat) && chat.SendMessages)
                        {
                            if (chat.MessagesAggregationTimeSec == 0)
                            {
                                await SendMessageAsync(chat.ChatId, alert.ToString()).ConfigureAwait(false);
                            }
                            else
                            {
                                IMessageBuilder builder = message is ScheduleAlertMessage ? chat.ScheduleMessageBuilder : chat.MessageBuilder;
                                builder.AddMessage(alert);
                            }
                        }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex);
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

                                await SendMessageAsync(chat.ChatId, notification).ConfigureAwait(false);
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

                            await _chatsManager.TryUpdate(update).ConfigureAwait(false);
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
                return permissions is null || 
                       ((!permissions.CanSendMessages || !permissions.CanSendMessages) &&
                        (!permissions.CanSendOtherMessages || !permissions.CanSendOtherMessages));
            }
        }

        //private void SendMarkdownMessageAsync(ChatId chat, string message) =>
        //    _bot?.SendTextMessageAsync(chat, message, ParseMode.MarkdownV2, cancellationToken: _tokenSource.Token);

        private async ValueTask SendMessageAsync(ChatId chat, string message)
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

                    await (_bot?.SendMessage(chat, message, cancellationToken: _tokenSource.Token) ?? Task.CompletedTask).ConfigureAwait(false);
                    break;
                }
                catch (ApiRequestException ex)
                {
                    _logger.Error($"An error ({ex.Message}) has been occurred while sending message #{retry} [{chat.Identifier}] {message}");
                    break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.Error($"An error ({ex.Message}) has been occurred while sending message #{retry} [{chat.Identifier}] {message}");

                    await Task.Delay(SendMessageRetryTimeout * retry, _tokenSource.Token).ConfigureAwait(false);
                    retry++;
                }
            }

        }
    }
}
