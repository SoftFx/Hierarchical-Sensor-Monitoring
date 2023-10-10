using HSMCommon.Collections;
using HSMServer.Model.Folders;
using HSMServer.Notification.Settings;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace HSMServer.Notifications
{
    internal sealed class AddressBook
    {
        private readonly ConcurrentDictionary<ChatId, TelegramChat> _chats = new();
        private readonly ConcurrentDictionary<Guid, InvitationToken> _tokens = new();
        //private readonly ConcurrentDictionary<ChatId, CHash<FolderModel>> _telegramBook = new();

        //internal ConcurrentDictionary<INotificatable, ConcurrentDictionary<ChatId, ChatSettings>> ServerBook { get; } = new(new NotificatableComparator());


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

        internal TelegramChat RegisterChat(Message message, InvitationToken token, FolderModel folder)
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

            RegisterChat(folder, chatModel);
            RemoveToken(token.Token);

            return chatModel;
        }

        internal void RegisterChat(FolderModel folder, TelegramChat chat)
        {
            //if (!ServerBook.ContainsKey(folder))
            //    ServerBook[folder] = new ConcurrentDictionary<ChatId, ChatSettings>();

            //if (!_telegramBook.ContainsKey(chat.ChatId))
            //    _telegramBook[chat.ChatId] = new CHash<INotificatable>(new NotificatableComparator());

            //ServerBook[folder].TryAdd(chat.ChatId, new ChatSettings(chat));
            //_telegramBook[chat.ChatId].Add(folder);
            _chats.TryAdd(chat.ChatId, chat);
        }

        internal TelegramChat RemoveChat(INotificatable entity, ChatId chatId)
        {
            TelegramChat removedChat = null;

            if (ServerBook.TryGetValue(entity, out var chats))
                if (chats.TryRemove(chatId, out _))
                    entity.Chats.TryRemove(chatId, out removedChat);

            RemoveEntity(entity, chatId);

            return removedChat;
        }

        internal void RemoveAllChats(INotificatable entity)
        {
            ServerBook.TryRemove(entity, out _);

            foreach (var (chatId, _) in entity.Chats)
                RemoveEntity(entity, chatId);
        }

        private void RemoveEntity(INotificatable entity, ChatId chatId)
        {
            //if (_telegramBook.TryGetValue(chatId, out var entities))
            //    entities.Remove(entity);
        }
    }
}
