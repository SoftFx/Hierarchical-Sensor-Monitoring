using HSMCommon.Constants;
using HSMServer.Core.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Configuration;
using NLog;
using System;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace HSMServer.Notifications
{
    public sealed class TelegramUpdateHandler : IUpdateHandler
    {
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
                    if (!parts[0].Contains(BotName))
                        return;

                    parts[0] = parts[0].Replace(BotName, string.Empty);
                }

                var response = parts[0] switch
                {
                    TelegramBotCommands.Start => StartBot(parts, message, isUserChat),
                    TelegramBotCommands.Info => EntitiesInfo(message.Chat, isUserChat),
                    TelegramBotCommands.Status => ServerStatus(),
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

            var response = new StringBuilder(1 << 5);

            if (_addressBook.TryGetToken(commandParts[1], out var token))
            {
                response.Append(token.Entity.BuildGreetings());

                if (token.ExpirationTime < DateTime.UtcNow)
                {
                    _addressBook.RemoveToken(token.Token);

                    response.Append("Sorry, your invitation token is expired.");
                }
                else
                {
                    _addressBook.RegisterChat(message, token, isUserChat);
                    token.Entity.UpdateEntity(_userManager, _cache);

                    response.Append(token.Entity.BuildSuccessfullResponse());
                }
            }
            else
                response.Append("Your token is invalid or expired.");

            return response.ToString();
        }

        private string EntitiesInfo(ChatId chat, bool isUserChat)
        {
            var response = new StringBuilder(1 << 6);
            var entityStr = isUserChat ? "user" : "product";

            response.AppendLine($"{(isUserChat ? "Authorized" : "Added")} {entityStr}(s) settings:");

            foreach (var entity in _addressBook.GetAuthorizedEntities(chat))
            {
                var telegramSetting = entity.Notifications.Telegram;

                response.AppendLine($"{entityStr} '{entity.Name}'");
                response.AppendLine($"    Messages delay: {telegramSetting.MessagesDelay}");
                response.AppendLine($"    Min status level: {telegramSetting.MessagesMinStatus}");
                response.AppendLine($"    Messages are enabled: {telegramSetting.MessagesAreEnabled}");
            }

            return response.ToString();
        }

        private static string ServerStatus()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;

            return $"HSM server {version.Major}.{version.Minor}.{version.Build} is alive";
        }
    }
}
