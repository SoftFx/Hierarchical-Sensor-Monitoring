using HSMServer.Core;
using HSMServer.Core.Cache;
using HSMServer.Extensions;
using HSMServer.Folders;
using HSMServer.Helpers;
using HSMServer.ServerConfiguration;
using NLog;
using System;
using System.Linq;
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
        private readonly TelegramConfig _config;
        private readonly TelegramBot _bot;

        private string BotName
        {
            get
            {
                return _config.BotName.StartsWith('@')?
                    _config.BotName.ToLower() :
                    $"@{_config.BotName.ToLower()}";
            }
        }


    internal TelegramUpdateHandler(TelegramBot bot, ITelegramChatsManager chatsManager, IFolderManager folderManager, TelegramConfig config)
        {
            _folderManager = folderManager;
            _chatsManager = chatsManager;
            _config = config;
            _bot = bot;
        }


        public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cToken)
        {
            if(update?.Message is null)
                return;

            if(update.Message.MigrateToChatId.HasValue)
            {
                _logger.Info($"Migrate to Supergroup: '{update.Message.Chat.Id}' -> '{update.Message.MigrateToChatId.Value}'");
                await _chatsManager.MigrateToSupergroup( update.Message.Chat.Id, update.Message.MigrateToChatId.Value);
                return;
            }

            try
            {
                if (update.Message.Chat is null ||
                    string.IsNullOrEmpty(update.Message.Text) ||
                    update.Type is not UpdateType.Message)
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
                    await botClient.SendMessage(message.Chat, response, parseMode: ParseMode.MarkdownV2, cancellationToken: cToken);
                else
                    _logger.Warn($"There is some invalid update message: {msgText}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Invalid message has been received: {update?.Type} - {update?.Message}. Exception: {ex}");
            }
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

            return MarkdownHelper.EscapeMarkdownV2( response.ToString());
        }

        private string EntitiesInfo(ChatId chatId)
        {
            var chat = _chatsManager.GetChatByChatId(chatId);
            var response = new StringBuilder(1 << 6);

            if (chat is not null)
            {
                response.Append($"*Messages delay*");
                response.AppendLine(MarkdownHelper.EscapeMarkdownV2($": {chat.MessagesAggregationTimeSec} seconds"));

                response.Append($"*Messages are enabled*");
                response.AppendLine(MarkdownHelper.EscapeMarkdownV2($": {chat.SendMessages}"));

                response.Append($"*Connected folders*");
                response.AppendLine(MarkdownHelper.EscapeMarkdownV2($": {string.Join(", ", chat.Folders.Select(f => _folderManager[f]?.Name).OrderBy(n => n))}"));
            }
            else
                response.AppendLine(MarkdownHelper.EscapeMarkdownV2("Chat is not found."));

            return response.ToString();
        }

        private static string Help() => MarkdownHelper.EscapeMarkdownV2(
            $"""
            Statuses: 
                {Core.OffTime.ToIcon()} (OffTime) -> {Core.Ok.ToIcon()} (Ok) -> {Core.Error.ToIcon()} (Error)
            """);

        private static string ServerStatus() => MarkdownHelper.EscapeMarkdownV2($"HSM server {ServerConfig.Version} is alive.");

        public Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, HandleErrorSource source, CancellationToken cancellationToken)
        {
            var message = $"Telegram bot '{botClient.BotId}' (source: '{source}') error: {exception}";
            _bot.OnErrorHandled(message);
            return Task.CompletedTask;
        }
    }
}
