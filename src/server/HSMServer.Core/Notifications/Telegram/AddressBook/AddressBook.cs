using System;
using System.Collections.Concurrent;
using Telegram.Bot.Types;
using User = HSMServer.Core.Model.Authentication.User;

namespace HSMServer.Core.Notifications
{
    internal sealed class AddressBook
    {
        private readonly ConcurrentDictionary<Guid, InvitationToken> _tokens = new();
        private readonly ConcurrentDictionary<Guid, ChatSettings> _book = new();
        private readonly ConcurrentDictionary<ChatId, Guid> _chats = new();


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
    }
}
