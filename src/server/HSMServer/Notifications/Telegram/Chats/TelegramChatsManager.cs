using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Folders;
using HSMServer.Model.Authentication;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.Notifications
{
    public sealed class TelegramChatsManager : ConcurrentStorage<TelegramChat, TelegramChatEntity, TelegramChatUpdate>, ITelegramChatsManager
    {
        private readonly IDatabaseCore _database;
        private readonly ITreeValuesCache _cache;
        private readonly IUserManager _userManager;
        private readonly IFolderManager _folderManager;


        protected override Action<TelegramChatEntity> AddToDb => _database.AddTelegramChat;

        protected override Action<TelegramChatEntity> UpdateInDb => _database.UpdateTelegramChat;

        protected override Action<TelegramChat> RemoveFromDb => chat => _database.RemoveTelegramChat(chat.Id.ToByteArray());

        protected override Func<List<TelegramChatEntity>> GetFromDb => _database.GetTelegramChats;


        public TelegramChatsManager(IDatabaseCore database, ITreeValuesCache cache, IUserManager userManager, IFolderManager folderManager)
        {
            _cache = cache;
            _database = database;
            _userManager = userManager;
            _folderManager = folderManager;
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

            foreach (var folder in _folderManager.GetValues())
                foreach (var chatId in folder.TelegramChats)
                    if (TryGetValue(chatId, out var chat))
                        chat.Folders.Add(folder.Id);
        }

        protected override TelegramChat FromEntity(TelegramChatEntity entity) => new(entity);

        [Obsolete("Should be removed after telegram chats migration")]
        private void ChatsMigration()
        {
            var chatsToResave = new Dictionary<Guid, TelegramChat>(1 << 4);
            var productChats = new Dictionary<Guid, HashSet<Guid>>(1 << 4);
            var productNamesToId = new Dictionary<string, Guid>(1 << 4);
            var usersToResave = new List<User>(1 << 4);

            foreach (var product in _cache.GetProducts())
                if (product.TelegramChats is null)
                {
                    productChats.Add(product.Id, new HashSet<Guid>());
                    productNamesToId.Add(product.DisplayName, product.Id);

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
                                    Type = ConnectedChatType.TelegramGroup,
                                    Name = oldChat.Name,
                                    SendMessages = true,
                                    AuthorizationTime = new DateTime(oldChat.AuthorizationTime),
                                    MessagesAggregationTimeSec = 60,
                                };

                                chatsToResave.Add(id, chat);
                            }

                            productChats[product.Id].Add(id);
                        }
                }

            if (productChats.Count > 0)
            {
                foreach (var user in _userManager.GetUsers())
                {
                    if (user.Notifications?.Telegram?.Chats?.Count > 0)
                        foreach (var (_, oldChat) in user.Notifications.Telegram.Chats)
                        {
                            var id = oldChat.Id;

                            if (!chatsToResave.ContainsKey(id))
                            {
                                var chat = new TelegramChat()
                                {
                                    Id = id,
                                    ChatId = oldChat.ChatId,
                                    Type = ConnectedChatType.TelegramPrivate,
                                    Name = oldChat.Name,
                                    SendMessages = true,
                                    AuthorizationTime = oldChat.AuthorizationTime,
                                    MessagesAggregationTimeSec = 60,
                                };

                                chatsToResave.Add(id, chat);
                            }
                        }

                    user.Notifications = new(new());
                    usersToResave.Add(user);
                }

                foreach (var sensor in _cache.GetSensors())
                    if (productNamesToId.TryGetValue(sensor.RootProductName, out var productId))
                    {
                        var sensorPolicies = sensor.Policies.ToList();
                        sensorPolicies.Add(sensor.Policies.TimeToLive);

                        foreach (var policy in sensorPolicies)
                            foreach (var (chatId, _) in policy.Destination.Chats)
                                productChats[productId].Add(chatId);
                    }

                foreach (var product in _cache.GetAllNodes())
                    if (productNamesToId.TryGetValue(product.RootProductName, out var parentId))
                        foreach (var (chatId, _) in product.Policies.TimeToLive.Destination.Chats)
                            productChats[parentId].Add(chatId);
            }

            foreach (var (_, chat) in chatsToResave)
                _database.AddTelegramChat(chat.ToEntity());

            foreach (var user in usersToResave)
                _userManager.UpdateUser(user);

            foreach (var (productId, chats) in productChats)
            {
                var update = new ProductUpdate()
                {
                    Id = productId,
                    TelegramChats = chats,
                    NotificationSettings = new() { TelegramSettings = null }
                };

                _cache.UpdateProduct(update);
            }
        }
    }
}
