using HSMCommon.Constants;
using HSMServer.Authentication;
using HSMServer.Configuration;
using HSMServer.Core;
using HSMServer.Core.Model.Policies;
using HSMServer.Model;
using HSMServer.Model.TreeViewModel;
using HSMServer.Notification.Settings;
using NLog;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace HSMServer.Notifications
{
    using Core = Core.Model.SensorStatus;


    public sealed class TelegramUpdateHandler : IUpdateHandler
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly AddressBook _addressBook;
        private readonly IUserManager _userManager;
        private readonly TreeViewModel _tree;
        private readonly IConfigurationProvider _config;

        private string BotName => $"@{_config.ReadOrDefault(ConfigurationConstants.BotName).Value.ToLower()}";


        internal TelegramUpdateHandler(AddressBook addressBook, IUserManager userManager,
            TreeViewModel tree, IConfigurationProvider config)
        {
            _addressBook = addressBook;
            _userManager = userManager;
            _tree = tree;
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
                    TelegramBotCommands.Icons => IconsInfo(),
                    _ => null,
                };

                if (!string.IsNullOrEmpty(response))
                    await botClient.SendTextMessageAsync(message.Chat, response, ParseMode.MarkdownV2, cancellationToken: cToken);
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
                    token.Entity.UpdateEntity(_userManager, _tree);

                    response.Append(token.Entity.BuildSuccessfullResponse());
                }
            }
            else
                response.Append("Your token is invalid or expired.");

            return response.ToString().EscapeMarkdownV2();
        }

        private string EntitiesInfo(ChatId chat, bool isUserChat)
        {
            var response = new StringBuilder(1 << 6);
            var entityStr = isUserChat ? "user" : "product";

            response.AppendLine($"{(isUserChat ? "Authorized" : "Added")} {entityStr}(s) settings:".EscapeMarkdownV2());

            foreach (var entity in _addressBook.GetAuthorizedEntities(chat))
            {
                var telegramSetting = entity.Notifications.UsedTelegram;

                response.AppendLine($"{entityStr} *{entity.Name.EscapeMarkdownV2()}*");
                response.AppendLine($"    Messages delay: {telegramSetting.MessagesDelaySec} sec".EscapeMarkdownV2());
                response.AppendLine($"    Min status level: {telegramSetting.MessagesMinStatus}".EscapeMarkdownV2());
                response.AppendLine($"    Messages are enabled: {telegramSetting.MessagesAreEnabled}".EscapeMarkdownV2());
            }

            return response.ToString();
        }

        private static string IconsInfo() =>
            $"""
            {Core.OffTime.ToIcon()} - received Offtime status
            {Core.Ok.ToIcon()} - received Ok status
            {Core.Warning.ToIcon()} - received Warning status
            {Core.Error.ToIcon()} - received Error status
            {ExpectedUpdateIntervalPolicy.PolicyIcon} - sensor update timeout
            ❓ - unknown status
            """.EscapeMarkdownV2();

        private static string ServerStatus() => $"HSM server {ServerConfig.Version} is alive.".EscapeMarkdownV2();
    }
}
