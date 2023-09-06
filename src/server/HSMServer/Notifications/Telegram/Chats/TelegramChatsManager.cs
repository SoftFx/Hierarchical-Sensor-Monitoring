﻿using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.DataLayer;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HSMServer.Notifications
{
    public sealed class TelegramChatsManager : ConcurrentStorage<TelegramChat, TelegramChatEntity, TelegramChatUpdate>, ITelegramChatsManager
    {
        private readonly IDatabaseCore _database;
        private readonly ITreeValuesCache _cache;
        private readonly IUserManager _userManager;


        protected override Action<TelegramChatEntity> AddToDb => _database.AddTelegramChat;

        protected override Action<TelegramChatEntity> UpdateInDb => _database.UpdateTelegramChat;

        protected override Action<TelegramChat> RemoveFromDb => chat => _database.RemoveTelegramChat(chat.Id.ToByteArray());

        protected override Func<List<TelegramChatEntity>> GetFromDb => _database.GetTelegramChats;


        public TelegramChatsManager(IDatabaseCore database, ITreeValuesCache cache, IUserManager userManager)
        {
            _cache = cache;
            _database = database;
            _userManager = userManager;
        }


        public void Dispose() { }

        public override async Task Initialize()
        {
            ChatsMigration();

            await base.Initialize();

            foreach (var (_, chat) in this)
            {
                if (_userManager.TryGetValueById(chat.AuthorId, out var author))
                    chat.Author = author.Name;
            }
        }

        protected override TelegramChat FromEntity(TelegramChatEntity entity) => new(entity);

        [Obsolete("Should be removed after telegram chats migration")]
        private void ChatsMigration()
        {
            var chatsToResave = new Dictionary<Guid, TelegramChat>(1 << 4);
            var productChats = new Dictionary<Guid, List<Guid>>(1 << 4);

            foreach (var product in _cache.GetProducts())
                if (product.TelegramChats is null)
                {
                    productChats.Add(product.Id, new List<Guid>());

                    if (product.NotificationsSettings?.TelegramSettings?.Chats?.Count > 0)
                        foreach (var oldChat in product.NotificationsSettings.TelegramSettings.Chats)
                        {
                            var id = new Guid(oldChat.SystemId);

                            if (!chatsToResave.ContainsKey(id))
                            {
                                var chat = new TelegramChat()
                                {
                                    Id = id,
                                    ChatId = oldChat.Id,
                                    Type = oldChat.IsUserChat ? ConnectedChatType.TelegramPrivate : ConnectedChatType.TelegramGroup,
                                    Name = oldChat.Name,
                                    SendMessages = true,
                                    AuthorizationTime = new DateTime(oldChat.AuthorizationTime),
                                    MessagesAggregationTime = 60,
                                };

                                chatsToResave.Add(id, chat);
                            }

                            productChats[product.Id].Add(id);
                        }
                }

            foreach (var (_, chat) in chatsToResave)
                _database.AddTelegramChat(chat.ToEntity());

            foreach (var (productId, chats) in productChats)
            {
                var update = new ProductUpdate()
                {
                    Id = productId,
                    TelegramChats = chats,
                    NotificationSettings = new() { TelegramSettings = null, AutoSubscription = false }
                };

                _cache.UpdateProduct(update);
            }
        }
    }
}
