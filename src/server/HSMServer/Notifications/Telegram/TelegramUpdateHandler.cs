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
        private readonly IFolderManager _folderManager;
        private readonly AddressBook _addressBook;
        private readonly TelegramConfig _config;
        private readonly ITreeValuesCache _cache;

        private string BotName => $"@{_config.BotName.ToLower()}";


        internal TelegramUpdateHandler(AddressBook addressBook, ITreeValuesCache cache, IFolderManager folderManager, TelegramConfig config)
        {
            _folderManager = folderManager;
            _addressBook = addressBook;
            _config = config;
            _cache = cache;
        }


        public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cToken)
        {
            if (update?.Type == UpdateType.Message)
            {
                var message = update?.Message;
                var msgText = message?.Text?.ToLowerInvariant();
                var parts = msgText?.Split(' ');

                if (parts == null || parts.Length == 0)
                    return;

                if (message?.Chat?.Type != ChatType.Private)
                {
                    if (!parts[0].Contains(BotName))
                        return;

                    parts[0] = parts[0].Replace(BotName, string.Empty);
                }

                var response = parts[0] switch
                {
                    TelegramBotCommands.Start => StartBot(parts, message),
                    TelegramBotCommands.Info => EntitiesInfo(message.Chat),
                    TelegramBotCommands.Server => ServerStatus(),
                    TelegramBotCommands.Help => Help(),
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


        private string StartBot(string[] commandParts, Message message)
        {
            if (commandParts.Length != 2)
                return null;

            var response = new StringBuilder(1 << 5);

            if (_addressBook.TryGetToken(commandParts[1], out var token))
            {
                response.Append($"Hi. ");

                if (token.ExpirationTime < DateTime.UtcNow)
                {
                    _addressBook.RemoveToken(token.Token);

                    response.Append("Sorry, your invitation token is expired.");
                }
                else if (!_folderManager.TryGetValue(token.Token, out var folder))
                {
                    _addressBook.RemoveToken(token.Token);

                    response.Append("Sorry, your token is invalid (folder doesn't exsist).");
                }
                else
                {
                    var newChat = _addressBook.RegisterChat(message, token, folder);
                    var isUserChat = newChat.Type is ConnectedChatType.TelegramPrivate;

                    if (newChat is not null)
                        _cache.AddNewChat(newChat.Id, newChat.Name, isUserChat ? null : token.User.Name);

                    response.Append($"Folder '{folder.Name}' is successfully added to ${(isUserChat ? "direct" : $"group by {token.User.Name}")}.");
                }
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
