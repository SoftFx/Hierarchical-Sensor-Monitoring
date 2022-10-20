using HSMServer.Core.Model;
using System;
using System.Collections.Concurrent;
using Telegram.Bot.Types;
using User = HSMServer.Core.Model.Authentication.User;

namespace HSMServer.Core.Notifications
{
    internal sealed class AddressBook
    {
        private readonly ConcurrentDictionary<Guid, InvitationToken> _tokens = new();
        //private readonly ConcurrentDictionary<string, HashSet<Guid>> _telegramBook = new();  // TODO: collection for bot commands


        internal ConcurrentDictionary<INotificatable, ConcurrentDictionary<ChatId, ChatSettings>> ServerBook { get; } = new(new NotificatableComparator());


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
            var chats = entity.GetChats();

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

            //if (!_telegramBook.ContainsKey(chat.Name))
            //    _telegramBook[chat.Name] = new HashSet<Guid>();

            ServerBook[entity].TryAdd(chat.Id, new ChatSettings(chat));
            //_telegramBook[chat.Name].Add(entity.Id);
        }

        internal void RemoveChat(INotificatable entity, ChatId chatId)
        {
            if (ServerBook.TryGetValue(entity, out var chats))
                if (chats.TryRemove(chatId, out _))
                    entity.GetChats().TryRemove(chatId, out _);
        }

        internal void RemoveAllChats(User user) => ServerBook.TryRemove(user, out _);
    }
}
