using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Exceptions;
using HSMServer.Core.Managers;
using HSMServer.Core.Notifications;
using HSMServer.Core.TableOfChanges;
using HSMServer.ConcurrentStorage;
using HSMServer.Folders;
using HSMServer.Notifications.Chats;
using HSMServer.ServerConfiguration;
using Chat = HSMServer.Notifications.Chats.Chat;
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
        private const string RedactedToken = "[redacted]";
        private const int MaxMessageLength = 1000;
        public const string BotIsNotRunningError = "Telegram Bot is not running.";
        internal const string BotIsDisposedError = "The Telegram bot is disposed.";

        private readonly static NLog.Logger _logger = NLog.LogManager.GetLogger(nameof(TelegramBot));

        private readonly ReceiverOptions _options = new()
        {
            AllowedUpdates = { }, // receive all update types
        };

        private readonly TelegramUpdateHandler _updateHandler;
        private readonly IChatsManager _chatsManager;
        private readonly IFolderManager _folderManager;
        private readonly TelegramConfig _config;

        private CancellationTokenSource _tokenSource = new();
        private ITelegramBotClient _bot;
        private readonly Func<string, ITelegramBotClient> _clientFactory;

        // Cancelled when the bot is disposed (#1830): a start in flight ends at once,
        // whenever it began, and none begins afterwards.
        private readonly CancellationTokenSource _lifetime = new();

        // One start at a time (#1830): the web UI's Restart button and the management
        // surface's token change can ask together, and two clients receiving at once would
        // answer every Telegram update twice. Held for a whole start, the stop of the
        // previous client included. Not reentrant: nothing under it may wait for it again.
        private readonly SemaphoreSlim _lifecycle = new(1, 1);

        // The bot that is running: the token it was started with and the username Telegram
        // reported for it then (#1830). The pair is kept, not read back from the
        // configuration — the saved token may change under a running bot (the web page
        // saves and restarts in two steps). Set by the start after its GetMe and cleared by
        // the stop, both under the gate above; read by anyone, as the one reference it is.
        private volatile RunningBot _running;


        private string BotToken => _config.BotToken;

        private bool CanSendNotifications => IsBotRunning && _config.IsRunning;


        public bool IsBotRunning => _bot is not null;

        // The username Telegram reported for the running bot, when it runs on exactly this
        // token; null otherwise, and the caller asks Telegram itself (GetBotUsernameAsync).
        internal string UsernameOfRunningBot(string token) =>
            _running is { } running && string.Equals(running.Token, token, StringComparison.Ordinal)
                ? running.Username
                : null;

        public event Action<string> ErrorHandled;
        public event Action<string, string> MessageSended;
        public event Action MessageSending;

        // clientFactory defaults to the real Telegram client; tests pass their own (#1824), so the
        // start path runs without the network.
        internal TelegramBot(IChatsManager chatsManager, IFolderManager folderManager, TelegramConfig config,
            Func<string, ITelegramBotClient> clientFactory = null)
        {
            _clientFactory = clientFactory ?? CreateClient;
            _folderManager = folderManager;
            _chatsManager = chatsManager;

            _config = config;

            _updateHandler = new(this, _chatsManager, _folderManager, config);
        }

        public async ValueTask DisposeAsync()
        {
            if (_lifetime.IsCancellationRequested)
                return;

            // Disposing comes before anything else: a start in flight ends at once, whenever
            // it began, and none begins afterwards.
            _lifetime.Cancel();

            // The stop outside the gate is what the common case needs — a start waiting for
            // Telegram is cancelled and lets go of the gate. A start that had got past its
            // own token by then is stopped under the gate, before the gate goes away.
            await StopBotAsync();

            await _lifecycle.WaitAsync();

            try
            {
                await StopBotAsync();
            }
            finally
            {
                _lifecycle.Release();
            }

            _lifecycle.Dispose();
            _lifetime.Dispose();
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
            var chat = _chatsManager.GetChatByChatId(chatId);

            if (chat != null)
                await SendMessageAsync(chat, message);
        }

        // A test message answers its outcome (#1826): ONE attempt, no retry loop — a broken
        // binding must report fast, not page for minutes like the delivery path.
        internal async Task<ChatDeliveryResult> SendTestWithResultAsync(Chat chat)
        {
            if (chat.TelegramChatId is null)
                return ChatDeliveryResult.Fail("The chat has no Telegram binding.");

            if (!CanSendNotifications)
                return ChatDeliveryResult.Fail(BotIsNotRunningError);

            try
            {
                MessageSending?.Invoke();

                const string testMessage = "Test message from HSM";

                await _bot.SendMessage(chat.TelegramChatId, MarkdownHelper.ConvertToMarkdownV2(testMessage), cancellationToken: _tokenSource.Token, parseMode: ParseMode.MarkdownV2);

                MessageSended?.Invoke(chat.Name, testMessage);
                _logger.Info("TSend: test message is sent");

                return ChatDeliveryResult.Ok();
            }
            catch (Exception ex)
            {
                var error = $"Send telegram: An error ({ex.Message}) has been occurred while sending the test message [{chat.Name}]";
                OnErrorHandled(error);

                return ChatDeliveryResult.Fail(error);
            }
        }

        // The one place the real Telegram client is built.
        private static ITelegramBotClient CreateClient(string token)
        {
            HttpClientHandler clientHandler = new() //todo: should be removed after live ubuntu update
            {
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true,
                SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
            };

            return new TelegramBotClient(token, new HttpClient(clientHandler))
            {
                Timeout = new TimeSpan(0, 5, 0) // 5 minutes timeout
            };
        }

        // Wipes the given token from third-party text (an exception message) before the text
        // reaches the log or the caller. The library does not put the request URL into exception
        // messages today; this guards against that changing (#1824).
        private static string RedactToken(string text, string token) =>
            string.IsNullOrEmpty(text) || string.IsNullOrEmpty(token)
                ? text
                : text.Replace(token, RedactedToken);

        internal async Task<string> StartBotAsync()
        {
            if (_lifetime.IsCancellationRequested)
                return BotIsDisposedError;

            await _lifecycle.WaitAsync();

            try
            {
                return await StartUnderGateAsync();
            }
            finally
            {
                _lifecycle.Release();
            }
        }

        // The body of a start, run with the lifecycle gate held.
        private async Task<string> StartUnderGateAsync()
        {
            if (IsBotRunning)
            {
                var message = await StopBotAsync();
                if (!string.IsNullOrEmpty(message))
                    return message;
            }

            if (!_config.IsValid)
                return ConfigurationsError;

            // Linked to the bot's lifetime: disposing the bot ends this start too, even if
            // it began a moment before.
            _tokenSource = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);

            // The token THIS start is attempted with: the redaction below must target it even when
            // the saved token changes while the request is in flight.
            var token = BotToken;
            _bot = _clientFactory(token);

            try
            {
                var me = await _bot.GetMe(_tokenSource.Token);

                _running = new RunningBot(token, me.Username);
            }
            catch (Exception exc)
            {
                _bot = null;

                // #1824: the token is a secret — the log says the credentials failed, never the
                // value, and third-party text (the exception) is scrubbed of it too.
                _logger.Error($"Invalid credentials, name = {_config?.BotName}");
                _logger.Error(RedactToken(exc.ToString(), token));

                return $"An error ({RedactToken(exc.Message, token)}) has been occurred while starting the Bot. Please check Bot configurations. The current state of the Bot is stopped.";
            }

            try
            {
                await _bot.SetMyCommands(TelegramBotCommands.Commands, cancellationToken: _tokenSource.Token);

                await ChatNamesSynchronization();

                _bot.StartReceiving(_updateHandler, _options, _tokenSource.Token);
            }
            catch (Exception exc)
            {
                // A start that breaks off after GetMe must not leave a client that counts as
                // running and receives nothing: it is stopped, and reported like a refusal.
                await StopBotAsync();

                _logger.Error(RedactToken(exc.ToString(), token));

                return RedactToken($"An error ({exc.Message}) has been occurred while starting the Bot. " +
                    "Please check Bot configurations. The current state of the Bot is stopped.", token);
            }

            return string.Empty;
        }

        // Asks Telegram who owns a token — the check of a bot credential BEFORE anything is
        // saved or restarted (#1830). Nothing of the running bot is touched: the question
        // goes through a client of its own. The outcome is answered, never thrown: the
        // caller decides what a refusal and a missing road to Telegram mean for its write.
        internal async Task<TelegramTokenProbe> GetBotUsernameAsync(string token)
        {
            ITelegramBotClient client;

            try
            {
                client = _clientFactory(token);
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException)
            {
                // The client refuses a token that is not shaped like one: no request leaves.
                return ProbeFailure(TelegramTokenProbe.Rejected(RedactToken(ex.Message, token)));
            }

            try
            {
                var me = await client.GetMe(CancellationToken.None);

                return TelegramTokenProbe.Accepted(me.Username);
            }
            catch (ApiRequestException ex) when (ex.ErrorCode is 401 or 404)
            {
                // Telegram answered, and about the token: unauthorized, or no such bot.
                return ProbeFailure(TelegramTokenProbe.Rejected(RedactToken(ex.Message, token)));
            }
            catch (Exception ex)
            {
                // No road to Telegram, a timeout, a rate limit: the token was not judged.
                return ProbeFailure(TelegramTokenProbe.Unreachable(RedactToken(ex.Message, token)));
            }
        }

        // Logs an attempt that ended in no username; the text is the bot's own, with the
        // token already wiped (#1824).
        private TelegramTokenProbe ProbeFailure(TelegramTokenProbe answer)
        {
            _logger.Warn($"Telegram was asked who owns a bot token and gave no username: {answer.Verdict} ({answer.Reason})");

            return answer;
        }

        internal async Task<string> StopBotAsync()
        {
            await _tokenSource.CancelAsync();

            var bot = _bot;
            _bot = null;
            _running = null;

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

        internal async Task DeliverAsync(AlertMessage message)
        {
            try
            {
                _logger.Debug($"TSend: DeliverAsync enter");

                if (!CanSendNotifications || !_folderManager.TryGetValue(message.FolderId, out var _))
                {
                    _logger.Info($"TSend: DeliverAsync can't send: CanSendNotifications={CanSendNotifications}");
                    return;
                }

                foreach (var alert in message)
                {
                    var chatIds = alert.Destination.Chats;

                    foreach (var chatId in chatIds)
                        if (_chatsManager.TryGetValue(chatId, out var chat) && chat.TelegramChatId is not null && chat.SendMessages)
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
                                chat.TelegramAccumulator.AddMessage(alert, message is ScheduleAlertMessage);

                                _logger.Info($"TSend: builder '{logAlert}'");
                            }
                        }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Send telegram: DeliverAsync error: {ex}");
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
                    if (chat.TelegramChatId is null)
                        continue;

                    try
                    {
                        if (chat.TelegramAccumulator.ShouldSend(chat.MessagesAggregationTimeSec))
                        {
                            foreach (var notification in chat.TelegramAccumulator.GetNotifications(chat.MessagesAggregationTimeSec))
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
                    if (chat.TelegramChatId is null)
                        continue;

                    try
                    {
                        ChatFullInfo telegramChat = await _bot.GetChat(chat.TelegramChatId, _tokenSource.Token);

                        if (ShouldGroupBeDeleted(telegramChat))
                        {
                            if (await _chatsManager.TryRemove(new RemoveRequest(chat.Id, InitiatorInfo.System)))
                                continue;
                        }

                        var chatName = chat.TelegramType is ConnectedChatType.TelegramPrivate ? telegramChat.Username : telegramChat.Title;
                        var chatDescription = telegramChat.Description;

                        if (chat.TelegramChatTitle != chatName || chat.TelegramChatDescription != chatDescription)
                        {
                            var update = new ChatUpdate()
                            {
                                Id = chat.Id,
                                TelegramChatTitle = chatName,
                                TelegramChatDescription = chatDescription,
                            };

                            await _chatsManager.TryUpdate(update);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Telegram chat '{chat.Id}' (title='{chat.TelegramChatTitle}') sync failed - {ex}");
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

        private async ValueTask SendMessageAsync(Chat chat, string message)
        {
            if (string.IsNullOrEmpty(message) || chat.TelegramChatId is null)
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

                    await (_bot?.SendMessage(chat.TelegramChatId, MarkdownHelper.ConvertToMarkdownV2(message), cancellationToken: _tokenSource.Token, parseMode: ParseMode.MarkdownV2) ?? Task.CompletedTask);

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

        private sealed record RunningBot(string Token, string Username);
    }
}
