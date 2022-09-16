using HSMCommon.Constants;
using HSMServer.Core.Authentication;
using HSMServer.Core.Configuration;
using HSMServer.Core.Model;
using NLog;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using User = HSMServer.Core.Model.Authentication.User;

namespace HSMServer.Core.Notifications
{
    public sealed class TelegramBot : IAsyncDisposable
    {
        private const string StartBotCommand = "/start";
        private const string ConfigurationsError = "Invalid Bot configurations.";

        private readonly AddressBook _addressBook = new();
        private readonly ReceiverOptions _options = new()
        {
            AllowedUpdates = { }, // receive all update types
        };

        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private readonly IUserManager _userManager;
        private readonly IConfigurationProvider _configurationProvider;

        private CancellationToken _token = CancellationToken.None;
        private TelegramBotClient _bot;

        private bool IsBotRunning => _bot is not null;

        private string BotName => _configurationProvider.ReadOrDefault(ConfigurationConstants.BotName).Value;

        private string BotToken => _configurationProvider.ReadOrDefault(ConfigurationConstants.BotToken).Value;

        private bool AreBotMessagesEnabled => bool.TryParse(_configurationProvider.ReadOrDefault(
            ConfigurationConstants.AreBotMessagesEnabled).Value, out var result) && result;


        internal TelegramBot(IUserManager userManager, IConfigurationProvider configurationProvider)
        {
            _userManager = userManager;
            _userManager.RemoveUserEvent += RemoveUserEventHandler;

            _configurationProvider = configurationProvider;

            FillAddressBook();
        }


        public string GetInvitationLink(User user) =>
            _addressBook.BuildInvitationToken(user).ToLink(BotName);

        public void RemoveChat(User user, long chatId)
        {
            _addressBook.RemoveChat(user, new ChatId(chatId));
            _userManager.UpdateUser(user);
        }

        public void SendTestMessage(long chatId, string message)
        {
            if (IsBotRunning)
                SendMessageAsync(chatId, message);
        }

        public async Task<string> StartBot()
        {
            if (IsBotRunning)
            {
                var message = await StopBot();
                if (!string.IsNullOrEmpty(message))
                    return message;
            }

            if (!IsValidBotConfigurations())
                return ConfigurationsError;

            _token = new CancellationToken();
            _bot = new TelegramBotClient(BotToken)
            {
                Timeout = new TimeSpan(0, 5, 0) // 5 minutes timeout
            };

            try
            {
                await _bot.GetMeAsync(_token);
            }
            catch (ApiRequestException exc)
            {
                _bot = null;
                return exc.Message;
            }

            _bot.StartReceiving(HandleUpdateAsync, HandleErrorAsync, _options, _token);
            ThreadPool.QueueUserWorkItem(MessageReceiver);

            return string.Empty;
        }

        public async Task<string> StopBot()
        {
            if (_token != default)
                _token.ThrowIfCancellationRequested();

            var bot = _bot;
            _bot = null;

            try
            {
                await bot?.CloseAsync(_token);
            }
            catch (Exception exc)
            {
                return exc.Message;
            }

            return string.Empty;
        }

        public async ValueTask DisposeAsync()
        {
            _userManager.RemoveUserEvent -= RemoveUserEventHandler;

            await StopBot();
        }

        internal void SendMessage(BaseSensorModel sensor, ValidationResult oldStatus)
        {
            if (IsBotRunning && AreBotMessagesEnabled)
                foreach (var (userId, chats) in _addressBook.ServerBook)
                {
                    var user = _userManager.GetUser(userId);

                    if (WhetherSendMessage(user, sensor, oldStatus))
                        foreach (var (_, chat) in chats)
                        {
                            if (user.Notifications.Telegram.MessagesDelay > 0)
                                chat.MessageBuilder.AddMessage(sensor);
                            else
                                SendMessageAsync(chat.ChatId, MessageBuilder.GetSingleMessage(sensor));
                        }
                }
        }

        private void FillAddressBook()
        {
            foreach (var user in _userManager.GetUsers())
                foreach (var (_, chat) in user.Notifications.Telegram.Chats)
                    _addressBook.RegisterChat(user, chat);
        }

        private async void MessageReceiver(object _)
        {
            while (IsBotRunning)
            {
                foreach (var (userId, chats) in _addressBook.ServerBook)
                {
                    var user = _userManager.GetUser(userId);
                    var userNotificationsDelay = user.Notifications.Telegram.MessagesDelay;

                    foreach (var (_, chat) in chats)
                        if (DateTime.UtcNow >= chat.MessageBuilder.LastSentTime.AddSeconds(userNotificationsDelay))
                        {
                            var message = chat.MessageBuilder.GetAggregateMessage();
                            if (!string.IsNullOrEmpty(message))
                                SendMessageAsync(chat.ChatId, message);
                        }
                }

                await Task.Delay(500, _token);
            }
        }

        private static bool WhetherSendMessage(User user, BaseSensorModel sensor, ValidationResult oldStatus)
        {
            var newStatus = sensor.ValidationResult;
            var minStatus = user.Notifications.Telegram.MessagesMinStatus;

            return user.Notifications.Telegram.MessagesAreEnabled &&
                   user.Notifications.IsSensorEnabled(sensor.Id) &&
                   !user.Notifications.IsSensorIgnored(sensor.Id) &&
                   newStatus != oldStatus &&
                   (newStatus.Result >= minStatus || oldStatus.Result >= minStatus);
        }

        private void SendMessageAsync(ChatId chat, string message) =>
            _bot?.SendTextMessageAsync(chat, message, cancellationToken: _token);

        private void RemoveUserEventHandler(User user) => _addressBook.RemoveAllChats(user);

        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cToken)
        {
            if (update?.Type == UpdateType.Message)
            {
                var message = update?.Message;
                var command = message?.Text?.ToLowerInvariant();

                if (command?.StartsWith(StartBotCommand) ?? false)
                {
                    var parts = command.Split(' ');
                    if (parts.Length != 2)
                        return;

                    var response = new StringBuilder(1 << 2);

                    if (_addressBook.TryGetToken(parts[1], out var token))
                    {
                        response.Append($"Hi, {token.User.UserName}. ");

                        if (token.ExpirationTime < DateTime.UtcNow)
                        {
                            _addressBook.RemoveToken(token.Token);

                            response.Append("Sorry, your invitation token is expired.");
                        }
                        else
                        {
                            _addressBook.RegisterChat(message, token);
                            _userManager.UpdateUser(token.User);

                            response.Append("You are succesfully authorized.");
                        }
                    }
                    else
                        response.Append("Your token is invalid or expired.");

                    await botClient.SendTextMessageAsync(message.Chat, response.ToString(), cancellationToken: cToken);
                }
                else
                {
                    _logger.Warn($"There is some invalid update message: {command}");
                }
            }
        }

        private Task HandleErrorAsync(ITelegramBotClient _, Exception ex, CancellationToken token)
        {
            _logger.Error($"There is some error in telegram bot: {ex}");

            return Task.CompletedTask;
        }

        private bool IsValidBotConfigurations() =>
            !string.IsNullOrEmpty(BotName) && !string.IsNullOrEmpty(BotToken);
    }
}
