using HSMServer.Core.Authentication;
using HSMServer.Core.Model;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Telegram.Bot.Types;
using User = HSMServer.Core.Model.Authentication.User;

namespace HSMServer.Core.Notifications
{
    internal sealed class AddressBook
    {
        private readonly ConcurrentDictionary<Guid, InvitationToken> _tokens = new();
        private readonly ConcurrentDictionary<Guid, ChatSettings> _book = new();
        private readonly ConcurrentDictionary<ChatId, Guid> _chats = new();

        private readonly IUserManager _userManager;


        internal AddressBook(IUserManager userManager)
        {
            _userManager = userManager;
        }


        internal InvitationToken GetInvitationToken(User user)
        {
            if (_book.TryGetValue(user.Id, out var chatSettings))
            {
                if (chatSettings.Chat is not null)
                    return InvitationToken.Empty;
                else
                    RemoveToken(chatSettings.Token);
            }

            var invitationToken = new InvitationToken(user);

            _tokens[invitationToken.Token] = invitationToken;
            _book[user.Id] = new ChatSettings(invitationToken.Token);

            return invitationToken;
        }

        internal bool TryGetToken(string tokenIdStr, out InvitationToken token)
        {
            token = InvitationToken.Empty;

            return Guid.TryParse(tokenIdStr, out var tokenId) && _tokens.TryGetValue(tokenId, out token);
        }

        internal void RemoveToken(Guid token) => _tokens.TryRemove(token, out _);

        internal void UserAuthorization(ChatId chat, InvitationToken token)
        {
            var user = token.User;

            user.Notifications.Telegram.Chat = chat;

            RemoveToken(token.Token);
            AddAuthorizedUser(user);
        }

        internal void AddAuthorizedUser(User user)
        {
            var chat = user.Notifications.Telegram.Chat;

            _book[user.Id] = new ChatSettings(chat);
            _chats[chat] = user.Id;
        }

        internal void RemoveAuthorizedUser(User user)
        {
            _book.TryRemove(user.Id, out _);
            _chats.TryRemove(user.Notifications.Telegram.Chat, out _);

            user.Notifications.Telegram.Chat = null;
        }

        internal List<(ChatId, string)> GetUsersChats(BaseSensorModel sensor, ValidationResult oldStatus, string productId)
        {
            var result = new List<(ChatId, string)>();

            foreach (var (userId, chatSettings) in _book)
            {
                if (chatSettings.Chat is null)
                    continue;

                var user = _userManager.GetUser(userId);

                if (WhetherSendMessage(user, sensor, oldStatus))
                {
                    if (user.Notifications.Telegram.MessagesDelay > 0)
                        chatSettings.MessageBuilder.AddMessage(sensor, productId);
                    else
                        result.Add((chatSettings.Chat, MessageBuilder.GetMessage(sensor)));
                }
            }

            return result;
        }

        internal List<(ChatId, string)> GetUsersChats(DateTime time)
        {
            var result = new List<(ChatId, string)>();

            foreach (var (userId, chatSettings) in _book)
            {
                if (chatSettings.Chat is null)
                    continue;

                if (time >= chatSettings.MessageBuilder.NotificationSendingTime)
                {
                    var message = chatSettings.MessageBuilder.GetAggregateMessage();
                    if (!string.IsNullOrEmpty(message))
                        result.Add((chatSettings.Chat, message));

                    var user = _userManager.GetUser(userId);

                    chatSettings.MessageBuilder.Clean(user.Notifications.Telegram.MessagesDelay);
                }
            }

            return result;
        }

        private static bool WhetherSendMessage(User user, BaseSensorModel sensor, ValidationResult oldStatus)
        {
            var newStatus = sensor.ValidationResult;
            var minStatus = user.Notifications.Telegram.MessagesMinStatus;

            return user.Notifications.Telegram.MessagesAreEnabled &&
                   newStatus != oldStatus &&
                   (newStatus.Result >= minStatus || oldStatus.Result >= minStatus);
        }
    }
}
