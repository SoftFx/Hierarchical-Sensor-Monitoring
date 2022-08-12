using HSMServer.Core.Authentication;
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

        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly AddressBook _addressBook = new();
        private readonly ReceiverOptions _options = new()
        {
            AllowedUpdates = { }, // receive all update types
        };

        private readonly IUserManager _userManager;

        private CancellationToken _token = CancellationToken.None;
        private ITelegramBotClient _bot;


        internal TelegramBot(IUserManager userManager)
        {
            _userManager = userManager;

            FillAuthorizedUsers();
        }


        public string GetInvitationLink(User user) =>
            _addressBook.GetInvitationToken(user).ToLink(BotName);

        public Task SendTestMessage(User user)
        {
            var testMessage = $"Test message for {user.UserName}";

            return _bot is not null
                ? _bot.SendTextMessageAsync(user.NotificationSettings.TelegramSettings.Chat, testMessage, cancellationToken: _token)
                : Task.CompletedTask;
        }

        public void RemoveAuthorizedUser(User user)
        {
            _addressBook.RemoveAuthorizedUser(user);
            _userManager.UpdateUser(user);
        }

        public async Task StartBot()
        {
            if (_bot is not null)
                return;

            await StopBot();

            _bot = new TelegramBotClient(BotToken);
            _token = new CancellationToken();

            _bot.StartReceiving(HandleUpdateAsync, HandleErrorAsync, _options, _token);
        }

        public Task StopBot()
        {
            if (_token != CancellationToken.None)
                _token.ThrowIfCancellationRequested();

            return _bot is not null ? _bot.CloseAsync(_token) : Task.CompletedTask;
        }

        public async ValueTask DisposeAsync() => await StopBot();

        internal void FillAuthorizedUsers()
        {
            var users = _userManager.GetUsers(u => u.NotificationSettings.TelegramSettings.Chat is not null);

            foreach (var user in users)
                _addressBook.AddAuthorizedUser(user.NotificationSettings.TelegramSettings.Chat, new InvitationToken(user));
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

                    if (Guid.TryParse(parts[1], out var tokenId) && _addressBook.TryGetToken(tokenId, out var token))
                    {
                        response.Append($"Hi, {token.User.UserName}. ");

                        if (token.ExpirationTime < DateTime.UtcNow)
                        {
                            _addressBook.RemoveToken(token);

                            response.Append("Sorry, your invitation token is expired.");
                        }
                        else
                        {
                            _addressBook.AddAuthorizedUser(message.Chat, token);
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

        private async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            _logger.Error($"There is some error in telegram bot: {exception}");
        }
    }
}
