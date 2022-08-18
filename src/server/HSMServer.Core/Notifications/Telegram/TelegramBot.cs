using HSMServer.Core.Authentication;
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

        // TODO: there are parameters from configuration provider
        private const string BotToken = "5424383384:AAHw56JEcaJa9wuxRgLp2UOjsknySLCRGfM";
        private const string BotName = "TestTestTestBoooooooootBot";

        private readonly AddressBook _addressBook = new();
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly ReceiverOptions _options = new()
        {
            AllowedUpdates = { }, // receive all update types
        };

        private readonly IUserManager _userManager;

        private CancellationToken _token = CancellationToken.None;
        private ITelegramBotClient _bot;

        private bool IsBotRunning => _bot is not null;


        internal TelegramBot(IUserManager userManager)
        {
            _userManager = userManager;
            _userManager.RemoveUserEvent += RemoveUserEventHandler;

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

        public void StartBot()
        {
            if (IsBotRunning)
                return;

            _bot = new TelegramBotClient(BotToken);
            _token = new CancellationToken();

            _bot.StartReceiving(HandleUpdateAsync, HandleErrorAsync, _options, _token);

            ThreadPool.QueueUserWorkItem(_ => MessageReceiver());
        }

        public Task StopBot()
        {
            if (_token != CancellationToken.None)
                _token.ThrowIfCancellationRequested();

            var bot = _bot;
            _bot = null;

            return bot?.CloseAsync(_token) ?? Task.CompletedTask;
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

        internal void SendMessage(BaseSensorModel sensor, ValidationResult oldStatus, string productId)
        {
            if (IsBotRunning)
                foreach (var (userId, chatSettings) in _addressBook.GetAuthorizedUsers)
                {
                    var user = _userManager.GetUser(userId);

                    if (WhetherSendMessage(user, sensor, oldStatus))
                    {
                        if (user.Notifications.Telegram.MessagesDelay > 0)
                            chatSettings.MessageBuilder.AddMessage(sensor, productId);
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
    }
}
