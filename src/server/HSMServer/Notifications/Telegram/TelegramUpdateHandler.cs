using HSMServer.Core;
using HSMServer.Core.Cache;
using HSMServer.Extensions;
using HSMServer.Folders;
using HSMServer.Notification.Settings;
using HSMServer.ServerConfiguration;
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
        private readonly ITelegramChatsManager _chatsManager;
        private readonly IFolderManager _folderManager;
        private readonly ITreeValuesCache _cache;
        private readonly TelegramConfig _config;

        private string BotName => $"@{_config.BotName.ToLower()}";


        internal TelegramUpdateHandler(ITelegramChatsManager chatsManager, ITreeValuesCache cache, IFolderManager folderManager, TelegramConfig config)
        {
            _folderManager = folderManager;
            _chatsManager = chatsManager;
            _config = config;
            _cache = cache;
        }


        public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cToken)
        {
            try
            {
                if (update is null || update.Message is null || update.Message.Chat is null || string.IsNullOrEmpty(update.Message.Text) || update.Type is not UpdateType.Message)
                    return;

                var message = update.Message;
                var msgText = message.Text.ToLowerInvariant();
                var parts = msgText.Split(' ');
                var command = parts[0];

                if (!message.FromPrivateChat())
                {
                    if (!command.Contains(BotName))
                        return;

                    command = command.Replace(BotName, string.Empty); // for group chats commands colled as command@botsname
                }

                var response = command switch
                {
                    TelegramBotCommands.Start => await StartBot(parts, message),
                    TelegramBotCommands.Info => EntitiesInfo(message.Chat),
                    TelegramBotCommands.Server => ServerStatus(),
                    TelegramBotCommands.Help => Help(),
                    _ => null,
                };

                if (!string.IsNullOrEmpty(response))
                    await botClient.SendTextMessageAsync(message.Chat, response, null, ParseMode.MarkdownV2, cancellationToken: cToken);
                else
                    _logger.Warn($"There is some invalid update message: {msgText}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Invalid message has been received: {update?.Type} - {update?.Message}. Exception: {ex}");
            }
        }

        public Task HandlePollingErrorAsync(ITelegramBotClient _, Exception ex, CancellationToken token)
        {
            _logger.Error($"There is some error in telegram bot: {ex}");

            return Task.CompletedTask;
        }


        private async Task<string> StartBot(string[] commandParts, Message message)
        {
            if (commandParts.Length != 2)
                return null;

            var response = new StringBuilder($"Hi. ");

            if (_chatsManager.TokenManager.TryRemoveToken(commandParts[1], out var token))
            {
                if (token.ExpirationTime >= DateTime.UtcNow)
                {
                    var folderName = await _chatsManager.TryConnect(message, token);

                    if (string.IsNullOrEmpty(folderName))
                        response.Append("Sorry, your token is invalid or folder doesn't exsist.");
                    else
                        response.Append($"Folder '{folderName}' is successfully added to {(message.FromPrivateChat() ? "direct" : $"group by {token.User.Name}")}.");
                }
                else
                    response.Append("Sorry, your invitation token is expired.");
            }
            else
                response.Append("Your token is invalid or expired.");

            return response.ToString().EscapeMarkdownV2();
        }

        // TODO: /info command should return chat settings (delay, enable) and all connected products
        private string EntitiesInfo(ChatId chat)
        {
            var response = new StringBuilder(1 << 6);

            response.AppendLine("Chat settings:".EscapeMarkdownV2());
            response.AppendLine($"Messages delay: 60 sec".EscapeMarkdownV2());
            response.AppendLine($"Messages are enabled: True".EscapeMarkdownV2());

            return response.ToString();
        }

        private static string Help() =>
            $"""
            Statuses: 
                {Core.OffTime.ToIcon()} (OffTime) -> {Core.Ok.ToIcon()} (Ok) -> {Core.Error.ToIcon()} (Error)
            """.EscapeMarkdownV2();

        private static string ServerStatus() => $"HSM server {ServerConfig.Version} is alive.".EscapeMarkdownV2();
    }
}
