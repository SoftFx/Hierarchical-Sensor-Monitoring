using HSMServer.Core.Model.Authentication;
using NLog;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;

namespace HSMServer.Core.Notifications
{
    public sealed class TelegramBot : IAsyncDisposable
    {
        private const string StartBotCommand = "/start";

        // TODO: there are parameters from configuration provider
        private const string BotToken = "5424383384:AAHw56JEcaJa9wuxRgLp2UOjsknySLCRGfM";
        private const string BotName = "TestTestTestBoooooooootBot";

        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly Dictionary<Guid, User> _addressBook = new();
        private readonly ReceiverOptions _options = new()
        {
            AllowedUpdates = { }, // receive all update types
        };

        private CancellationToken _token = CancellationToken.None;
        private ITelegramBotClient _bot;


        public string GetInvitationLink(User user)
        {
            if (user.Token != Guid.Empty)
                _addressBook.Remove(user.Token);

            user.Token = Guid.NewGuid();

            _addressBook[user.Token] = user;

            return $"https://t.me/{BotName}?start={user.Token}";
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


        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Telegram.Bot.Types.Update update, CancellationToken cancellationToken)
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

                    var token = Guid.Parse(parts[1]);

                    if (_addressBook.TryGetValue(token, out var user))
                    {
                        await botClient.SendTextMessageAsync(message.Chat, $"Hi, {user.UserName}", cancellationToken: cancellationToken);

                        var x = 0;
                        while (++x < 5)
                        {
                            await botClient.SendTextMessageAsync(message.Chat, $"Test message #{x}", cancellationToken: cancellationToken);

                            await Task.Delay(200, cancellationToken);
                        }

                        return;
                    }
                }
            }
        }

        private async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            _logger.Error($"There is some error in telegram bot: {exception}");
        }
    }
}
