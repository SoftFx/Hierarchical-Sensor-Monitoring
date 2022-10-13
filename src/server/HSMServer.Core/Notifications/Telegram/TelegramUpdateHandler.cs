using HSMCommon.Constants;
using HSMServer.Core.Authentication;
using HSMServer.Core.Configuration;
using NLog;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace HSMServer.Core.Notifications
{
    public sealed class TelegramUpdateHandler : IUpdateHandler
    {
        private const string StartBotCommand = "/start";

        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly AddressBook _addressBook;
        private readonly IUserManager _userManager;
        private readonly IConfigurationProvider _config;

        private string BotName => _config.ReadOrDefault(ConfigurationConstants.BotName).Value;


        internal TelegramUpdateHandler(AddressBook addressBook, IUserManager userManager, IConfigurationProvider config)
        {
            _addressBook = addressBook;
            _userManager = userManager;
            _config = config;
        }


        public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cToken)
        {
            if (update?.Type == UpdateType.Message)
            {
                var message = update?.Message;
                var isUserChat = message?.Chat?.Type == ChatType.Private;
                var command = message?.Text?.ToLowerInvariant();
                var parts = command?.Split(' ');

                if (!isUserChat && parts.Length > 0 && parts[0] == $"@{BotName.ToLower()}")
                    parts = parts[1..];

                if (parts.Length == 2 && parts[0].StartsWith(StartBotCommand))
                {
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
                            _addressBook.RegisterChat(message, token, isUserChat);
                            _userManager.UpdateUser(token.User);

                            response.Append("You are succesfully authorized.");
                        }
                    }
                    else
                        response.Append("Your token is invalid or expired.");

                    await botClient.SendTextMessageAsync(message.Chat, response.ToString(), cancellationToken: cToken);
                }
                else if (isUserChat)
                {
                    _logger.Warn($"There is some invalid update message: {command}");
                }
            }
        }

        public Task HandlePollingErrorAsync(ITelegramBotClient _, Exception ex, CancellationToken token)
        {
            _logger.Error($"There is some error in telegram bot: {ex}");

            return Task.CompletedTask;
        }
    }
}
