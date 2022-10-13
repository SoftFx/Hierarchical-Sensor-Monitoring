﻿using HSMServer.Core.Model;
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


        internal ConcurrentDictionary<Guid, ConcurrentDictionary<ChatId, ChatSettings>> ServerBook { get; } = new();


        internal Guid BuildInvitationToken(User user)
        {
            var invitationToken = new InvitationToken(user);

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
            var user = token.User;
            var chats = user.Notifications.Telegram.Chats;

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
                    RegisterChat(user, chatModel);
            }

            RemoveToken(token.Token);
        }

        internal void RegisterChat(User user, TelegramChat chat)
        {
            if (!ServerBook.ContainsKey(user.Id))
                ServerBook[user.Id] = new ConcurrentDictionary<ChatId, ChatSettings>();

            //if (!_telegramBook.ContainsKey(chat.Name))
            //    _telegramBook[chat.Name] = new HashSet<Guid>();

            ServerBook[user.Id].TryAdd(chat.Id, new ChatSettings(user.Id, chat));
            //_telegramBook[chat.Name].Add(user.Id);
        }

        internal void RemoveChat(User user, ChatId chatId)
        {
            if (ServerBook.TryGetValue(user.Id, out var chats))
                if (chats.TryRemove(chatId, out _))
                    user.Notifications.Telegram.Chats.TryRemove(chatId, out _);
        }

        internal void RemoveAllChats(User user) => ServerBook.TryRemove(user.Id, out _);
    }
}
