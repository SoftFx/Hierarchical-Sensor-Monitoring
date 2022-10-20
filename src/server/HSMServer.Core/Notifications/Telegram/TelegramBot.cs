using HSMCommon.Constants;
using HSMServer.Core.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.Entities;
using HSMServer.Core.Configuration;
using HSMServer.Core.Model;
using System;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using User = HSMServer.Core.Model.Authentication.User;

namespace HSMServer.Core.Notifications
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

        private CancellationToken _token = CancellationToken.None;
        private TelegramBotClient _bot;

        private bool IsBotRunning => _bot is not null;

        private string BotName => _configurationProvider.ReadOrDefault(ConfigurationConstants.BotName).Value;

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


        public string GetInvitationLink(User user) =>
            $"https://t.me/{BotName}?start={_addressBook.BuildInvitationToken(user)}";

        public string GetStartCommandForGroup(ProductModel product) =>
            $"@{BotName} /start {_addressBook.BuildInvitationToken(product)}";

        public async Task<string> GetChatLink(long chatId)
        {
            var link = await _bot.CreateChatInviteLinkAsync(new ChatId(chatId), cancellationToken: _token);

            return link.InviteLink;
        }

        public void RemoveOldInvitationTokens() => _addressBook.RemoveOldTokens();

        public void RemoveChat(INotificatable entity, long chatId)
        {
            _addressBook.RemoveChat(entity, new ChatId(chatId));

            if (entity is User user)
                _userManager.UpdateUser(user);
            else if (entity is ProductModel product)
                _cache.UpdateProduct(product);
        }

        public void SendTestMessage(long chatId, string message)
        {
            if (IsBotRunning)
                SendMessageAsync(chatId, message);
        }

        public async Task<string> StartBot()
        {
            if (IsBotRunning)
            {
                var message = await StopBot();
                if (!string.IsNullOrEmpty(message))
                    return message;
            }

            if (!IsValidBotConfigurations())
                return ConfigurationsError;

            _token = new CancellationToken();
            _bot = new TelegramBotClient(BotToken)
            {
                Timeout = new TimeSpan(0, 5, 0) // 5 minutes timeout
            };

            try
            {
                await _bot.GetMeAsync(_token);
            }
            catch (ApiRequestException exc)
            {
                _bot = null;
                return exc.Message;
            }

            _bot.StartReceiving(_updateHandler, _options, _token);
            ThreadPool.QueueUserWorkItem(MessageReceiver);

            return string.Empty;
        }

        public async Task<string> StopBot()
        {
            if (_token != default)
                _token.ThrowIfCancellationRequested();

            var bot = _bot;
            _bot = null;

            try
            {
                await bot?.CloseAsync(_token);
            }
            catch (Exception exc)
            {
                return exc.Message;
            }

            return string.Empty;
        }

        public async ValueTask DisposeAsync()
        {
            _userManager.RemoveUserEvent -= RemoveUserEventHandler;

            await StopBot();
        }

        private void FillAddressBook()
        {
            foreach (var user in _userManager.GetUsers())
                foreach (var (_, chat) in user.Notifications.Telegram.Chats)
                    _addressBook.RegisterChat(user, chat);

            foreach (var product in _cache.GetProducts(null))
                foreach (var (_, chat) in product.Notifications.Telegram.Chats)
                    _addressBook.RegisterChat(product, chat);
        }

        private void SendMessage(BaseSensorModel sensor, ValidationResult oldStatus)
        {
            if (IsBotRunning && AreBotMessagesEnabled)
                foreach (var (userId, chats) in _addressBook.ServerBook)
                {
                    //var user = _userManager.GetUser(userId);

                    //if (WhetherSendMessage(user, sensor, oldStatus))
                    //    foreach (var (_, chat) in chats)
                    //    {
                    //        if (user.Notifications.Telegram.MessagesDelay > 0)
                    //            chat.MessageBuilder.AddMessage(sensor);
                    //        else
                    //            SendMessageAsync(chat.ChatId, MessageBuilder.GetSingleMessage(sensor));
                    //    }
                }
        }

        private async void MessageReceiver(object _)
        {
            while (IsBotRunning)
            {
                foreach (var (userId, chats) in _addressBook.ServerBook)
                {
                    //var user = _userManager.GetUser(userId);
                    //var userNotificationsDelay = user.Notifications.Telegram.MessagesDelay;

                    //foreach (var (_, chat) in chats)
                    //    if (DateTime.UtcNow >= chat.MessageBuilder.LastSentTime.AddSeconds(userNotificationsDelay))
                    //    {
                    //        var message = chat.MessageBuilder.GetAggregateMessage();
                    //        if (!string.IsNullOrEmpty(message))
                    //            SendMessageAsync(chat.ChatId, message);
                    //    }
                }

                await Task.Delay(500, _token);
            }
        }

        private static bool WhetherSendMessage(User user, BaseSensorModel sensor, ValidationResult oldStatus)
        {
            var newStatus = sensor.ValidationResult;
            var minStatus = user.Notifications.Telegram.MessagesMinStatus;

            return user.Notifications.Telegram.MessagesAreEnabled &&
                   user.Notifications.IsSensorEnabled(sensor.Id) &&
                   !user.Notifications.IsSensorIgnored(sensor.Id) &&
                   newStatus != oldStatus &&
                   (newStatus.Result >= minStatus || oldStatus.Result >= minStatus);
        }

        private void SendMessageAsync(ChatId chat, string message) =>
            _bot?.SendTextMessageAsync(chat, message, cancellationToken: _token);

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
