using HSMCommon.Constants;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Configuration;
using HSMServer.Core.Model;
using HSMServer.Extensions;
using System;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using User = HSMServer.Model.Authentication.User;
using Telegram.Bot.Types.Enums;

namespace HSMServer.Notifications
{
    public sealed class TelegramBot : IAsyncDisposable
    {
        private const string ConfigurationsError = "Invalid Bot configurations.";

        private readonly AddressBook _addressBook = new();
        private readonly ReceiverOptions _options = new()
        {
            AllowedUpdates = { }, // receive all update types
        };

        private readonly IUserManager _userManager;
        private readonly ITreeValuesCache _cache;
        private readonly IConfigurationProvider _configurationProvider;
        private readonly TelegramUpdateHandler _updateHandler;

        private CancellationTokenSource _tokenSource = new();
        private TelegramBotClient _bot;

        private bool IsBotRunning => _bot is not null;

        private string BotToken => _configurationProvider.ReadOrDefault(ConfigurationConstants.BotToken).Value;

        private bool AreBotMessagesEnabled => bool.TryParse(_configurationProvider.ReadOrDefault(
            ConfigurationConstants.AreBotMessagesEnabled).Value, out var result) && result;


        internal TelegramBot(IUserManager userManager, ITreeValuesCache cache,
            IConfigurationProvider configurationProvider)
        {
            _userManager = userManager;
            _userManager.RemoveUserEvent += RemoveUserEventHandler;

            _cache = cache;
            _cache.ChangeProductEvent += RemoveProductEventHandler;
            _cache.NotifyAboutChangesEvent += SendMessage;

            _configurationProvider = configurationProvider;
            _updateHandler = new(_addressBook, _userManager, _cache, _configurationProvider);

            FillAddressBook();
        }


        public async ValueTask DisposeAsync()
        {
            _userManager.RemoveUserEvent -= RemoveUserEventHandler;

            await StopBot();
        }
        
        internal string BotName => _configurationProvider.ReadOrDefault(ConfigurationConstants.BotName).Value;
        
        internal string GetInvitationLink(User user) =>
            $"https://t.me/{BotName}?start={_addressBook.BuildInvitationToken(user)}";

        internal string GetStartCommandForGroup(ProductModel product) =>
            $"{TelegramBotCommands.Start}@{BotName} {_addressBook.BuildInvitationToken(product)}";

        internal async Task<string> GetChatLink(long chatId)
        {
            var link = await _bot.CreateChatInviteLinkAsync(new ChatId(chatId), cancellationToken: _tokenSource.Token);

            return link.InviteLink;
        }
        
        internal void RemoveOldInvitationTokens() => _addressBook.RemoveOldTokens();

        internal void RemoveChat(INotificatable entity, long chatId)
        {
            _addressBook.RemoveChat(entity, new ChatId(chatId));
            entity.UpdateEntity(_userManager, _cache);
        }

        internal void SendTestMessage(long chatId, string message)
        {
            if (IsBotRunning)
                _bot?.SendTextMessageAsync(chatId, message, cancellationToken: _tokenSource.Token);
        }

        internal async Task<string> StartBot()
        {
            if (IsBotRunning)
            {
                var message = await StopBot();
                if (!string.IsNullOrEmpty(message))
                    return message;
            }

            if (!IsValidBotConfigurations())
                return ConfigurationsError;

            _tokenSource = new CancellationTokenSource();
            _bot = new TelegramBotClient(BotToken)
            {
                Timeout = new TimeSpan(0, 5, 0) // 5 minutes timeout
            };

            try
            {
                await _bot.GetMeAsync(_tokenSource.Token);
            }
            catch (ApiRequestException exc)
            {
                _bot = null;
                return $"An error ({exc.Message}) has been occurred while starting the Bot. Please check Bot configurations. The current state of the Bot is stopped.";
            }

            await _bot.SetMyCommandsAsync(TelegramBotCommands.Commands, cancellationToken: _tokenSource.Token);

            _bot.StartReceiving(_updateHandler, _options, _tokenSource.Token);
            ThreadPool.QueueUserWorkItem(async _ => await MessageReceiver());

            return string.Empty;
        }

        internal async Task<string> StopBot()
        {
            _tokenSource.Cancel();

            var bot = _bot;
            _bot = null;

            try
            {
                await bot?.DeleteWebhookAsync();
                await bot?.CloseAsync();
            }
            catch (Exception exc)
            {
                return $"An error ({exc.Message}) has been occurred while stopping the Bot. The current state of the Bot is stopped. Try to restart Bot again.";
            }

            return string.Empty;
        }

        private void FillAddressBook()
        {
            foreach (var user in _userManager.GetUsers())
                foreach (var (_, chat) in user.Notifications.Telegram.Chats)
                    _addressBook.RegisterChat(user, chat);

            foreach (var product in _cache.GetProducts())
                foreach (var (_, chat) in product.Notifications.Telegram.Chats)
                    _addressBook.RegisterChat(product, chat);
        }

        private static bool WhetherSendMessage(INotificatable entity, BaseSensorModel sensor, ValidationResult oldStatus)
        {
            var newStatus = sensor.ValidationResult;
            var minWebStatus = entity.Notifications.Telegram.MessagesMinStatus.ToClient();

            return entity.NotificationsEnabled(sensor) &&
                   newStatus != oldStatus &&
                   (newStatus.Result.ToClient() >= minWebStatus || oldStatus.Result.ToClient() >= minWebStatus);
        }

        private void SendMessage(BaseSensorModel sensor, ValidationResult oldStatus)
        {
            if (IsBotRunning && AreBotMessagesEnabled)
                foreach (var (entity, chats) in _addressBook.ServerBook)
                {
                    if (WhetherSendMessage(entity, sensor, oldStatus))
                        foreach (var (_, chat) in chats)
                        {
                            if (entity.Notifications.Telegram.MessagesDelay > 0)
                                chat.MessageBuilder.AddMessage(sensor);
                            else
                                SendMarkdownMessageAsync(chat.ChatId, MessageBuilder.GetSingleMessage(sensor));
                        }
                }
        }

        private async Task MessageReceiver()
        {
            while (IsBotRunning)
            {
                foreach (var (entity, chats) in _addressBook.ServerBook)
                {
                    var notificationsDelay = entity.Notifications.Telegram.MessagesDelay;

                    foreach (var (_, chat) in chats)
                        if (DateTime.UtcNow >= chat.MessageBuilder.LastSentTime.AddSeconds(notificationsDelay))
                        {
                            var message = chat.MessageBuilder.GetAggregateMessage();
                            if (!string.IsNullOrEmpty(message))
                                SendMarkdownMessageAsync(chat.ChatId, message);
                        }
                }

                if (_tokenSource.IsCancellationRequested)
                    break;

                try
                {
                    await Task.Delay(500, _tokenSource.Token);
                }
                catch { }
            }
        }

        private void SendMarkdownMessageAsync(ChatId chat, string message) =>
            _bot?.SendTextMessageAsync(chat, message, ParseMode.MarkdownV2, cancellationToken: _tokenSource.Token);

        private void RemoveUserEventHandler(User user) => _addressBook.RemoveAllChats(user);

        private void RemoveProductEventHandler(ProductModel product, TransactionType transaction)
        {
            if (transaction == TransactionType.Delete)
                _addressBook.RemoveAllChats(product);
        }

        private bool IsValidBotConfigurations() =>
            !string.IsNullOrEmpty(BotName) && !string.IsNullOrEmpty(BotToken);
    }
}
