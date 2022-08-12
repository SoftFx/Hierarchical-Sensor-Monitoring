using System;
using System.Collections.Generic;
using Telegram.Bot.Types;
using User = HSMServer.Core.Model.Authentication.User;

namespace HSMServer.Core.Notifications
{
    internal sealed class AddressBook
    {
        private readonly Dictionary<Guid, InvitationToken> _tokens = new();
        private readonly Dictionary<Guid, ChatSettings> _book = new();
        private readonly Dictionary<ChatId, Guid> _chats = new();


        internal InvitationToken GetInvitationToken(User user)
        {
            if (_book.TryGetValue(user.Id, out var chatSettings))
            {
                if (chatSettings.Chat is not null)
                    return chatSettings.Token;
                else
                    RemoveToken(chatSettings.Token);
            }

            var invitationToken = new InvitationToken(user);

            _tokens[invitationToken.Token] = invitationToken;
            _book[user.Id] = new ChatSettings(invitationToken);

            return invitationToken;
        }

        internal bool TryGetToken(Guid tokenId, out InvitationToken token) =>
            _tokens.TryGetValue(tokenId, out token);

        internal void RemoveToken(InvitationToken token)
        {
            _book.Remove(token.User.Id);
            _tokens.Remove(token.Token);
        }

        internal void AddAuthorizedUser(ChatId chat, InvitationToken token)
        {
            _tokens.Remove(token.Token);

            token.User.NotificationSettings.TelegramSettings.Chat = chat;
            token.TagTokenAsSuccessfullyUsed();

            if (!_book.TryGetValue(token.User.Id, out _))
                _book[token.User.Id] = new ChatSettings(token);

            _book[token.User.Id].Chat = chat;
            _chats[chat] = token.User.Id;
        }
    }
}
