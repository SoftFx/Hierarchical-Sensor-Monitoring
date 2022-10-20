using HSMCommon.Constants;
using HSMServer.Core.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.Entities;
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
using User = HSMServer.Core.Model.Authentication.User;

namespace HSMServer.Core.Notifications
{
    public sealed class TelegramUpdateHandler : IUpdateHandler
    {
        private const string StartBotCommand = "/start";

        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly AddressBook _addressBook;
        private readonly IUserManager _userManager;
        private readonly ITreeValuesCache _cache;
        private readonly IConfigurationProvider _config;

        private string BotName => $"@{_config.ReadOrDefault(ConfigurationConstants.BotName).Value.ToLower()}";


        internal TelegramUpdateHandler(AddressBook addressBook, IUserManager userManager,
            ITreeValuesCache cache, IConfigurationProvider config)
        {
            _addressBook = addressBook;
            _userManager = userManager;
            _cache = cache;
            _config = config;
        }


        public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cToken)
        {
            if (update?.Type == UpdateType.Message)
            {
                var message = update?.Message;
                var isUserChat = message?.Chat?.Type == ChatType.Private;
                var msgText = message?.Text?.ToLowerInvariant();
                var parts = msgText?.Split(' ');

                if (parts == null || parts.Length == 0)
                    return;

                if (!isUserChat)
                {
                    if (parts[0] != BotName)
                        return;

                    parts = parts[1..];
                }

                var response = parts[0] switch
                {
                    StartBotCommand => StartBot(parts, message, isUserChat),
                    _ => null,
                };

                if (!string.IsNullOrEmpty(response))
                    await botClient.SendTextMessageAsync(message.Chat, response, cancellationToken: cToken);
                else
                    _logger.Warn($"There is some invalid update message: {msgText}");
            }
        }

        public Task HandlePollingErrorAsync(ITelegramBotClient _, Exception ex, CancellationToken token)
        {
            _logger.Error($"There is some error in telegram bot: {ex}");

            return Task.CompletedTask;
        }


        private string StartBot(string[] commandParts, Message message, bool isUserChat)
        {
            if (commandParts.Length != 2)
                return null;

            var response = new StringBuilder(1 << 2);

            if (_addressBook.TryGetToken(commandParts[1], out var token))
            {
                response.Append(token.Entity.BuildStartCommandGreetings());

                if (token.ExpirationTime < DateTime.UtcNow)
                {
                    _addressBook.RemoveToken(token.Token);

                    response.Append("Sorry, your invitation token is expired.");
                }
                else
                {
                    _addressBook.RegisterChat(message, token, isUserChat);

                    if (token.Entity is User user)
                        _userManager.UpdateUser(user);
                    else if (token.Entity is ProductModel product)
                        _cache.UpdateProduct(product);

                    response.Append(token.Entity.BuildStartCommandSuccessfullResponse());
                }
            }
            else
                response.Append("Your token is invalid or expired.");

            return response.ToString();
        }
    }
}
