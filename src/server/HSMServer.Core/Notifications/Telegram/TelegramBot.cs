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
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using User = HSMServer.Core.Model.Authentication.User;

namespace HSMServer.Core.Notifications
{
    public sealed class TelegramBot : IAsyncDisposable
    {
        private const string StartBotCommand = "/start";

        private string BotToken; //"5424383384:AAHw56JEcaJa9wuxRgLp2UOjsknySLCRGfM";
        private string BotName; //"TestTestTestBoooooooootBot";
        private bool AreBotMessagesEnabled;

        private readonly AddressBook _addressBook;
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly ReceiverOptions _options = new()
        {
            AllowedUpdates = { }, // receive all update types
        };

        private readonly IUserManager _userManager;

        private CancellationToken _token = CancellationToken.None;
        private ITelegramBotClient _bot;


        internal TelegramBot(IUserManager userManager, IConfigurationProvider configurationProvider)
        {
            _userManager = userManager;
            _addressBook = new(userManager);

            BotToken = configurationProvider.ReadOrDefaultConfigurationObject(ConfigurationConstants.BotToken).Value;
            BotName = configurationProvider.ReadOrDefaultConfigurationObject(ConfigurationConstants.BotName).Value;
            AreBotMessagesEnabled = bool.TryParse(configurationProvider.ReadOrDefaultConfigurationObject(
                ConfigurationConstants.AreBotMessagesEnabled).Value, out var result) && result;

            FillAuthorizedUsers();
        }

        public string GetInvitationLink(User user) =>
            _addressBook.GetInvitationToken(user).ToLink(BotName);

        public void RemoveAuthorizedUser(User user)
        {
            _addressBook.RemoveAuthorizedUser(user);
            _userManager.UpdateUser(user);
        }

        public Task SendTestMessage(User user)
        {
            var chat = user.Notifications.Telegram.Chat;
            var testMessage = $"Test message for {user.UserName}";

            return _bot?.SendTextMessageAsync(chat, testMessage, cancellationToken: _token) ?? Task.CompletedTask;
        }

        internal void SendMessage(BaseSensorModel sensor)
        {
            if (_bot is not null)
            {
                foreach (var chat in _addressBook.GetUsersChats(sensor))
                {
                    var message = new StringBuilder(1 << 2);
                    message.Append($"Sensor (product name: {sensor.ProductName}, path {sensor.Path}) ");
                    message.Append($"has status {sensor.ValidationResult.Result}");

                    if (!sensor.ValidationResult.IsSuccess)
                        message.Append($" ({sensor.ValidationResult.Message})");

                    _bot?.SendTextMessageAsync(chat, message.ToString(), cancellationToken: _token);
                }
            }
        }

        public void StartBot()
        {
            if (_bot is not null)
                return;

            if (string.IsNullOrEmpty(BotName) || string.IsNullOrEmpty(BotToken))
                return;

            _bot = new TelegramBotClient(BotToken);
            _token = new CancellationToken();

            _bot.StartReceiving(HandleUpdateAsync, HandleErrorAsync, _options, _token);
        }

        public Task StopBot()
        {
            if (_token != CancellationToken.None)
                _token.ThrowIfCancellationRequested();

            var bot = _bot;
            _bot = null;

            return bot?.CloseAsync(_token) ?? Task.CompletedTask;
        }

        public async ValueTask DisposeAsync() => await StopBot();

        internal void FillAuthorizedUsers()
        {
            var users = _userManager.GetUsers(u => u.Notifications.Telegram.Chat is not null);

            foreach (var user in users)
                _addressBook.AddAuthorizedUser(user);
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
    }
}
