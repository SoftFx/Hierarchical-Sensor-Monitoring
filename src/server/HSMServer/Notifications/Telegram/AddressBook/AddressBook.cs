using HSMServer.Notification.Settings;
using HSMServer.Notifications.Telegram;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Telegram.Bot.Types;

namespace HSMServer.Notifications
{
    internal sealed class AddressBook
    {
        private readonly ConcurrentDictionary<Guid, InvitationToken> _tokens = new();
        private readonly ConcurrentDictionary<ChatId, HashSet<INotificatable>> _telegramBook = new();

        internal ConcurrentDictionary<INotificatable, ConcurrentDictionary<ChatId, ChatSettings>> ServerBook { get; } = new(new NotificatableComparator());


        internal HashSet<INotificatable> GetAuthorizedEntities(ChatId chat) => _telegramBook.GetValueOrDefault(chat) ?? new();

        internal Guid BuildInvitationToken(INotificatable entity)
        {
            var invitationToken = new InvitationToken(entity);

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

        internal void RegisterChat(Message message, InvitationToken token, bool isUserChat)
        {
            var entity = token.Entity;
            var chats = entity.Chats;

            if (!chats.ContainsKey(message.Chat))
            {
                var chatModel = new TelegramChat()
                {
                    Id = message.Chat,
                    Name = isUserChat ? message.From.Username : message.Chat.Title,
                    IsUserChat = isUserChat,
                    AuthorizationTime = DateTime.UtcNow,
                };

                if (chats.TryAdd(message.Chat, chatModel))
                    RegisterChat(entity, chatModel);
            }

            RemoveToken(token.Token);
        }

        internal void RegisterChat(INotificatable entity, TelegramChat chat)
        {
            if (!ServerBook.ContainsKey(entity))
                ServerBook[entity] = new ConcurrentDictionary<ChatId, ChatSettings>();

            if (!_telegramBook.ContainsKey(chat.Id))
                _telegramBook[chat.Id] = new HashSet<INotificatable>();

            ServerBook[entity].TryAdd(chat.Id, new ChatSettings(chat));
            _telegramBook[chat.Id].Add(entity);
        }

        internal void RemoveChat(INotificatable entity, ChatId chatId)
        {
            if (ServerBook.TryGetValue(entity, out var chats))
                if (chats.TryRemove(chatId, out _))
                    entity.Chats.TryRemove(chatId, out _);

            RemoveEntity(entity, chatId);
        }

        internal void RemoveAllChats(INotificatable entity)
        {
            ServerBook.TryRemove(entity, out _);

            foreach (var (chatId, _) in entity.Chats)
                RemoveEntity(entity, chatId);
        }

        private void RemoveEntity(INotificatable entity, ChatId chatId)
        {
            if (_telegramBook.TryGetValue(chatId, out var entities))
                entities.Remove(entity);
        }
    }
}
