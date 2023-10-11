using System;
using System.Collections.Concurrent;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace HSMServer.Notifications
{
    internal sealed class AddressBook
    {
        private readonly ConcurrentDictionary<ChatId, TelegramChat> _chats = new();
        private readonly ConcurrentDictionary<Guid, InvitationToken> _tokens = new();


        internal Guid BuildInvitationToken(Guid folderId, Model.Authentication.User user)
        {
            var invitationToken = new InvitationToken(folderId, user);

            _tokens[invitationToken.Token] = invitationToken;

            return invitationToken.Token;
        }

        internal void RemoveOldTokens()
        {
            foreach (var (tokenId, token) in _tokens)
                if (DateTime.UtcNow >= token.ExpirationTime.AddHours(1))
                    _tokens.TryRemove(tokenId, out _);
        }

        internal bool TryGetToken(string tokenIdStr, out InvitationToken token)
        {
            token = InvitationToken.Empty;

            return Guid.TryParse(tokenIdStr, out var tokenId) && _tokens.TryGetValue(tokenId, out token);
        }

        internal void RemoveToken(Guid token) => _tokens.TryRemove(token, out _);

        internal TelegramChat RegisterChat(Message message, InvitationToken token)
        {
            if (!_chats.TryGetValue(message.Chat, out var chatModel))
            {
                bool isUserChat = message?.Chat?.Type == ChatType.Private;

                chatModel = new TelegramChat()
                {
                    ChatId = message.Chat,
                    AuthorId = token.User.Id,
                    Author = token.User.Name,
                    AuthorizationTime = DateTime.UtcNow,
                    Name = isUserChat ? message.From.Username : message.Chat.Title,
                    Type = isUserChat ? ConnectedChatType.TelegramPrivate : ConnectedChatType.TelegramGroup,
                };
            }

            RegisterChat(chatModel);
            RemoveToken(token.Token);

            return chatModel;
        }

        internal void RegisterChat(TelegramChat chat) => _chats.TryAdd(chat.ChatId, chat);

        internal void RemoveChat(ChatId chatId) => _chats.TryRemove(chatId, out _);
    }
}
