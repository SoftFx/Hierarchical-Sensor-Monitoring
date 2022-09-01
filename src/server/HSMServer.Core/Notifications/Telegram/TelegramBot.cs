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
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly ReceiverOptions _options = new()
        {
            AllowedUpdates = { }, // receive all update types
        };

        private readonly IUserManager _userManager;
        private readonly IConfigurationProvider _configurationProvider;

        private CancellationToken _token = CancellationToken.None;
        private ITelegramBotClient _bot;

        private bool IsBotRunning => _bot is not null;
        private string BotName => _configurationProvider.ReadOrDefaultConfigurationObject(ConfigurationConstants.BotName).Value;
        private string BotToken => _configurationProvider.ReadOrDefaultConfigurationObject(ConfigurationConstants.BotToken).Value;
        private bool AreBotMessagesEnabled => bool.TryParse(_configurationProvider.ReadOrDefaultConfigurationObject(
            ConfigurationConstants.AreBotMessagesEnabled).Value, out var result) && result;


        internal TelegramBot(IUserManager userManager, IConfigurationProvider configurationProvider)
        {
            _userManager = userManager;
            _userManager.RemoveUserEvent += RemoveUserEventHandler;

            _configurationProvider = configurationProvider;

            FillAuthorizedUsers();
        }


        public string GetInvitationLink(User user) =>
            _addressBook.GetInvitationToken(user).ToLink(BotName);

        public void RemoveAuthorizedUser(User user)
        {
            _addressBook.RemoveAuthorizedUser(user);
            _userManager.UpdateUser(user);
        }

        public void SendTestMessage(User user)
        {
            if (IsBotRunning)
                SendMessageAsync(user.Notifications.Telegram.Chat, $"Test message for {user.UserName}");
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

            _bot = new TelegramBotClient(BotToken);
            _token = new CancellationToken();

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
            ThreadPool.QueueUserWorkItem(_ => MessageReceiver());

            return string.Empty;
        }

        public async Task<string> StopBot()
        {
            if (_token != CancellationToken.None)
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

        internal void FillAuthorizedUsers()
        {
            var users = _userManager.GetUsers(u => u.Notifications.Telegram.Chat is not null);

            foreach (var user in users)
                _addressBook.AddAuthorizedUser(user);
        }

        internal void SendMessage(BaseSensorModel sensor, ValidationResult oldStatus)
        {
            if (IsBotRunning && AreBotMessagesEnabled)
                foreach (var (userId, chatSettings) in _addressBook.GetAuthorizedUsers)
                {
                    var user = _userManager.GetUser(userId);

                    if (WhetherSendMessage(user, sensor, oldStatus))
                    {
                        if (user.Notifications.Telegram.MessagesDelay > 0)
                            chatSettings.MessageBuilder.AddMessage(sensor);
                        else
                            SendMessageAsync(chatSettings.Chat, MessageBuilder.GetSingleMessage(sensor));
                    }
                }
        }

        private async Task MessageReceiver()
        {
            while (IsBotRunning)
            {
                foreach (var (userId, chatSettings) in _addressBook.GetAuthorizedUsers)
                {
                    var user = _userManager.GetUser(userId);
                    var userNotificationsDelay = user.Notifications.Telegram.MessagesDelay;

                    if (DateTime.UtcNow >= chatSettings.MessageBuilder.LastSentTime.AddSeconds(userNotificationsDelay))
                    {
                        var message = chatSettings.MessageBuilder.GetAggregateMessage();
                        if (!string.IsNullOrEmpty(message))
                            SendMessageAsync(chatSettings.Chat, message);
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
                   user.Notifications.EnabledSensors.Contains(sensor.Id) &&
                   !user.Notifications.IgnoredSensors.ContainsKey(sensor.Id) &&
                   newStatus != oldStatus &&
                   (newStatus.Result >= minStatus || oldStatus.Result >= minStatus);
        }

        private void SendMessageAsync(ChatId chat, string message) =>
            _bot?.SendTextMessageAsync(chat, message, cancellationToken: _token);

        private void RemoveUserEventHandler(User user)
        {
            if (_addressBook.IsUserAuthorized(user))
                _addressBook.RemoveAuthorizedUser(user);
        }

        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            if (update.Type == UpdateType.Message)
            {
                var message = update.Message;
                var command = message?.Text?.ToLowerInvariant();

                if (command.StartsWith(StartBotCommand))
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
                            _addressBook.UserAuthorization(message.Chat, token);
                            _userManager.UpdateUser(token.User);

                            response.Append("You are succesfully authorized.");
                        }
                    }
                    else
                        response.Append("Your token is invalid.");

                    await botClient.SendTextMessageAsync(message.Chat, response.ToString(), cancellationToken: cancellationToken);
                }
            }
        }

        private Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            _logger.Error($"There is some error in telegram bot: {exception}");

            return Task.CompletedTask;
        }

        private bool IsValidBotConfigurations() =>
            !string.IsNullOrEmpty(BotName) && !string.IsNullOrEmpty(BotToken);
    }
}
