using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.TableOfChanges;
using HSMServer.Folders;
using HSMServer.Model.Folders;
using HSMServer.Model.TreeViewModel;
using HSMServer.Notifications.Telegram.Tokens;
using HSMServer.ServerConfiguration;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using User = HSMServer.Model.Authentication.User;

namespace HSMServer.Notifications
{
    public sealed class TelegramChatsManager : ConcurrentStorage<TelegramChat, TelegramChatEntity, TelegramChatUpdate>, ITelegramChatsManager
    {
        private readonly ConcurrentDictionary<ChatId, TelegramChat> _telegramChatIds = new();

        private readonly TelegramConfig _config;
        private readonly IDatabaseCore _database;
        private readonly ITreeValuesCache _cache;
        private readonly IUserManager _userManager;
        private readonly IFolderManager _folderManager;

        public TokenManager TokenManager { get; } = new();

        internal string BotName => _config.BotName;


        protected override Action<TelegramChatEntity> AddToDb => _database.AddTelegramChat;

        protected override Action<TelegramChatEntity> UpdateInDb => _database.UpdateTelegramChat;

        protected override Action<TelegramChat> RemoveFromDb => chat => _database.RemoveTelegramChat(chat.Id.ToByteArray());

        protected override Func<List<TelegramChatEntity>> GetFromDb => _database.GetTelegramChats;


        public event Func<Guid, Guid, string, Task<string>> ConnectChatToFolder;


        public TelegramChatsManager(IDatabaseCore database, ITreeValuesCache cache, IUserManager userManager, IFolderManager folderManager, IServerConfig config, TreeViewModel _) // TODO: remove TreeViewModel after telegram chats migration. this module is for filling folder.Products
        {
            _cache = cache;
            _database = database;
            _config = config.Telegram;
            _userManager = userManager;
            _folderManager = folderManager;
        }


        public void Dispose() { }


        public async override Task<bool> TryAdd(TelegramChat model) =>
            await base.TryAdd(model) && _telegramChatIds.TryAdd(model.ChatId, model);

        public async override Task<bool> TryRemove(RemoveRequest remove) =>
            TryGetValue(remove.Id, out var chat) && await base.TryRemove(remove) && _telegramChatIds.TryRemove(chat.ChatId, out _);


        public override async Task Initialize()
        {
            await ChatsMigration();

            await base.Initialize();

            foreach (var (_, chat) in this)
            {
                if (_userManager.TryGetValueById(chat.AuthorId, out var author))
                    chat.Author = author.Name;

                _telegramChatIds.TryAdd(chat.ChatId, chat);
            }
        }


        public string GetInvitationLink(Guid folderId, User user) =>
            $"https://t.me/{BotName}?start={TokenManager.BuildInvitationToken(folderId, user)}";

        public string GetGroupInvitation(Guid folderId, User user) =>
            $"{TelegramBotCommands.Start}@{BotName} {TokenManager.BuildInvitationToken(folderId, user)}";

        public async Task<string> TryConnect(Message message, InvitationToken token)
        {
            var isChatExist = _telegramChatIds.TryGetValue(message.Chat, out var chat);

            if (!isChatExist)
            {
                bool isUserChat = message?.Chat?.Type == ChatType.Private;

                chat = new TelegramChat()
                {
                    ChatId = message.Chat,
                    AuthorId = token.User.Id,
                    Author = token.User.Name,
                    AuthorizationTime = DateTime.UtcNow,
                    Name = isUserChat ? message.From.Username : message.Chat.Title,
                    Type = isUserChat ? ConnectedChatType.TelegramPrivate : ConnectedChatType.TelegramGroup,
                };
            }

            var folderName = await ConnectChatToFolder?.Invoke(chat.Id, token.FolderId, token.User.Name);

            if (!string.IsNullOrEmpty(folderName))
            {
                if (!isChatExist)
                    await TryAdd(chat);

                chat.Folders.Add(token.FolderId);
            }

            return folderName;
        }

        public void AddFolderToChats(Guid folderId, List<Guid> chats)
        {
            foreach (var chatId in chats)
                if (TryGetValue(chatId, out var chat))
                    chat.Folders.Add(folderId);
        }

        public async Task RemoveFolderFromChats(Guid folderId, List<Guid> chats, InitiatorInfo initiator)
        {
            foreach (var chatId in chats)
                if (TryGetValue(chatId, out var chat))
                {
                    chat.Folders.Remove(folderId);

                    if (chat.Folders.Count == 0)
                        await TryRemove(new(chatId, initiator));
                }

            _cache.RemoveChatsFromPolicies(folderId, chats, initiator);
        }

        public void RemoveFolderHandler(FolderModel folder, InitiatorInfo initiator) =>
            _ = RemoveFolderFromChats(folder.Id, folder.TelegramChats.ToList(), initiator);


        protected override TelegramChat FromEntity(TelegramChatEntity entity) => new(entity);

        [Obsolete("Should be removed after telegram chats migration")]
        private async Task ChatsMigration()
        {
            var chatsToResave = new Dictionary<Guid, TelegramChat>(1 << 4);
            var folderChats = new Dictionary<Guid, HashSet<Guid>>(1 << 4);
            var productNamesToFolderId = new Dictionary<string, Guid>(1 << 4);
            var usersToResave = new List<User>(1 << 4);
            var sensorsToResave = new List<SensorUpdate>(1 << 5); // sensors and nodes without folder should have empty chats in alerts
            var nodesToResave = new List<ProductUpdate>(1 << 5); // sensors and nodes without folder should have empty chats in alerts

            var sensorsToRemove = new List<Guid>(1 << 2); // remove all sensors without parent

            foreach (var folder in _folderManager.GetValues())
                if (folder.TelegramChats is null)
                {
                    folderChats.Add(folder.Id, new HashSet<Guid>());

                    foreach (var (_, product) in folder.Products)
                    {
                        productNamesToFolderId.Add(product.Name, folder.Id);

                        if (product.Notifications?.Telegram?.Chats?.Count > 0)
                            foreach (var (_, oldChat) in product.Notifications.Telegram.Chats)
                            {
                                var id = oldChat.Id;

                                if (!chatsToResave.ContainsKey(id))
                                {
                                    var chat = new TelegramChat()
                                    {
                                        Id = id,
                                        ChatId = oldChat.ChatId,
                                        Type = ConnectedChatType.TelegramGroup,
                                        Name = oldChat.Name,
                                        SendMessages = true,
                                        AuthorizationTime = oldChat.AuthorizationTime,
                                        MessagesAggregationTimeSec = 60,
                                    };

                                    chatsToResave.Add(id, chat);
                                }

                                folderChats[folder.Id].Add(id);
                            }
                    }
                }

            if (folderChats.Count > 0)
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
                {
                    if (sensor.Parent is null)
                    {
                        sensorsToRemove.Add(sensor.Id);
                        continue;
                    }

                    if (productNamesToFolderId.TryGetValue(sensor.RootProductName, out var folderId))
                    {
                        var sensorPolicies = sensor.Policies.ToList();
                        sensorPolicies.Add(sensor.Policies.TimeToLive);

                        foreach (var policy in sensorPolicies)
                            foreach (var (chatId, _) in policy.Destination.Chats)
                                folderChats[folderId].Add(chatId);
                    }
                    else
                    {
                        var update = new SensorUpdate()
                        {
                            Id = sensor.Id,
                            Policies = sensor.Policies.Select(BuildUpdateWithEmptyDestination).ToList(),
                            TTLPolicy = BuildUpdateWithEmptyDestination(sensor.Policies.TimeToLive),
                            Initiator = InitiatorInfo.AsSystemForce(),
                        };

                        sensorsToResave.Add(update);
                    }
                }

                foreach (var product in _cache.GetAllNodes())
                {
                    if (productNamesToFolderId.TryGetValue(product.RootProductName, out var folderId))
                        foreach (var (chatId, _) in product.Policies.TimeToLive.Destination.Chats)
                            folderChats[folderId].Add(chatId);
                    else
                    {
                        var update = new ProductUpdate()
                        {
                            Id = product.Id,
                            TTLPolicy = BuildUpdateWithEmptyDestination(product.Policies.TimeToLive),
                            Initiator = InitiatorInfo.AsSystemForce(),
                        };

                        nodesToResave.Add(update);
                    }
                }
            }

            foreach (var (_, chat) in chatsToResave)
                _database.AddTelegramChat(chat.ToEntity());

            foreach (var (folderId, chats) in folderChats)
            {
                var update = new FolderUpdate()
                {
                    Id = folderId,
                    TelegramChats = chats,
                    Initiator = InitiatorInfo.AsSystemForce(),
                };

                await _folderManager.TryUpdate(update);
            }

            foreach (var update in sensorsToResave)
                _cache.TryUpdateSensor(update, out _);

            foreach (var update in nodesToResave)
                _cache.UpdateProduct(update);

            foreach (var user in usersToResave)
                await _userManager.UpdateUser(user);

            if (chatsToResave.Count > 0)
                foreach (var product in _cache.GetProducts())
                {
                    var update = new ProductUpdate()
                    {
                        Id = product.Id,
                        NotificationSettings = new() { TelegramSettings = null },
                        Initiator = InitiatorInfo.AsSystemForce(),
                    };

                    _cache.UpdateProduct(update);
                }

            foreach (var sensorId in sensorsToRemove)
                _cache.RemoveSensor(sensorId);
        }

        private PolicyUpdate BuildUpdateWithEmptyDestination(Policy policy) =>
            new()
            {
                Id = policy.Id,
                Conditions = policy.Conditions.Select(c => new PolicyConditionUpdate(c.Operation, c.Property, c.Target, c.Combination)).ToList(),
                Sensitivity = policy.Sensitivity,
                Status = policy.Status,
                Template = policy.Template,
                Icon = policy.Icon,
                IsDisabled = policy.IsDisabled,
                Destination = new(),
                Initiator = InitiatorInfo.AsSystemForce(),
            };
    }
}
