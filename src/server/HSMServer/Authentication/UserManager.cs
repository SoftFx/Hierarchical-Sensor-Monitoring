﻿using HSMCommon;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Helpers;
using HSMServer.Model.Authentication;
using HSMServer.Notifications.Telegram;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.Authentication
{
    public sealed class UserManager : ConcurrentStorage<User, UserEntity, UserUpdate>, IUserManager
    {
        private const string DefaultUserUsername = "default";

        private readonly IDatabaseCore _databaseCore;
        private readonly ITreeValuesCache _treeValuesCache;
        private readonly ILogger<UserManager> _logger;


        protected override Action<UserEntity> AddToDb => _databaseCore.AddUser;

        protected override Action<UserEntity> UpdateInDb => _databaseCore.UpdateUser;

        protected override Action<User> RemoveFromDb => user => _databaseCore.RemoveUser(user.ToEntity());

        protected override Func<List<UserEntity>> GetFromDb => _databaseCore.GetUsers;


        public UserManager(IDatabaseCore databaseCore, ITreeValuesCache cache, ILogger<UserManager> logger)
        {
            _databaseCore = databaseCore;
            _logger = logger;

            _treeValuesCache = cache;
            _treeValuesCache.ChangeProductEvent += ChangeProductEventHandler;
            _treeValuesCache.ChangeSensorEvent += ChangeSensorEventHandler;
        }


        public void Dispose()
        {
            _treeValuesCache.ChangeProductEvent -= ChangeProductEventHandler;
            _treeValuesCache.ChangeSensorEvent -= ChangeSensorEventHandler;
        }

        public Task<bool> AddUser(string userName, string passwordHash, bool isAdmin, List<(Guid, ProductRoleEnum)> productRoles = null)
        {
            User user = new(userName)
            {
                Password = passwordHash,
                IsAdmin = isAdmin,
            };

            if (productRoles != null && productRoles.Count > 0)
                user.ProductsRoles = productRoles;

            return TryAdd(user);
        }

        public Task<bool> UpdateUser(User user) =>
            ContainsKey(user.Id) ? TryUpdate(user) : TryAdd(user);

        public async Task RemoveUser(string userName)
        {
            if (TryGetIdByName(userName, out var userId))
                await TryRemove(userId);
            else
                _logger.LogWarning($"There are no users with name={userName} to remove");
        }

        public bool TryAuthenticate(string login, string password)
        {
            var passwordHash = HashComputer.ComputePasswordHash(password);

            bool IsAskedUser(KeyValuePair<Guid, User> userPair)
            {
                var user = userPair.Value;
                return user.Name.Equals(login) && !string.IsNullOrEmpty(user.Password) && user.Password.Equals(passwordHash);
            }

            var existingUser = this.SingleOrDefault(IsAskedUser);

            return existingUser.Value != null;
        }

        public List<User> GetViewers(Guid productId)
        {
            var result = new List<User>(1 << 2);

            if (IsEmpty)
                return result;

            foreach (var (_, user) in this)
            {
                var pair = user.ProductsRoles?.FirstOrDefault(r => r.Item1.Equals(productId));
                if (pair != null && pair.Value.Item1 != Guid.Empty)
                    result.Add(user);
            }

            return result;
        }

        public List<User> GetManagers(Guid productId)
        {
            var result = new List<User>(1 << 2);

            if (IsEmpty)
                return result;

            foreach (var (_, user) in this)
                if (ProductRoleHelper.IsManager(productId, user.ProductsRoles))
                    result.Add(user);

            return result;
        }

        public IEnumerable<User> GetUsers(Func<User, bool> filter = null) => filter != null ? Values.Where(filter) : Values;

        public override async Task Initialize()
        {
            await base.Initialize();

            if (Count == 0)
            {
                await AddDefaultUser();
                _logger.LogInformation("Default user has been added.");
            }

            TelegramChatsMigration();
            PoliciesDestinationMigration();

            _logger.LogInformation($"Read users from database, users count = {Count}.");
        }

        protected override User FromEntity(UserEntity entity) => new(entity);

        private Task<bool> AddDefaultUser() =>
            AddUser(DefaultUserUsername,
                    HashComputer.ComputePasswordHash(DefaultUserUsername),
                    true);

        private void ChangeProductEventHandler(ProductModel product, ActionType transaction)
        {
            if (transaction == ActionType.Delete)
            {
                var updatedUsers = new List<User>(1 << 2);

                foreach (var (_, user) in this)
                {
                    var removedRolesCount = user.ProductsRoles.RemoveAll(role => role.Item1 == product.Id);
                    if (removedRolesCount == 0)
                        continue;

                    updatedUsers.Add(user);
                }

                foreach (var userToEdit in updatedUsers)
                    TryUpdate(userToEdit);
            }
        }

        private void ChangeSensorEventHandler(BaseSensorModel sensor, ActionType transaction)
        {
            if (transaction == ActionType.Delete)
            {
                foreach (var (_, user) in this)
                {
                    if (!user.Notifications.RemoveSensor(sensor.Id))
                        continue;

                    TryUpdate(user);
                }
            }
        }

        [Obsolete("Should be removed after telegram chat IDs migration")]
        private void TelegramChatsMigration()
        {
            _logger.LogInformation($"Starting users telegram chats migration...");

            var usersToResave = new HashSet<Guid>();
            var chatIds = new Dictionary<Telegram.Bot.Types.ChatId, Guid>(1 << 4);

            foreach (var (_, user) in this)
                foreach (var (chatId, chat) in user.Notifications.Telegram.Chats)
                    if (chat.SystemId == Guid.Empty)
                    {
                        if (chatIds.TryGetValue(chatId, out var systemChatId))
                            chat.SystemId = systemChatId;
                        else
                        {
                            chat.SystemId = Guid.NewGuid();
                            chatIds.Add(chatId, chat.SystemId);
                        }

                        usersToResave.Add(user.Id);
                    }

            foreach (var userId in usersToResave)
                if (TryGetValue(userId, out var user))
                    _databaseCore.UpdateUser(user.ToEntity());

            _logger.LogInformation($"{usersToResave.Count} users telegram chats migration is finished");
        }

        [Obsolete("Should be removed after policies chats migration")]
        private void PoliciesDestinationMigration()
        {
            _logger.LogInformation($"Starting policies destination migration for sensors...");

            var policiesToResave = new Dictionary<Guid, Policy>(1 << 8);
            var sensorsToResave = new HashSet<Guid>();

            var products = new Dictionary<string, NotificationSettingsEntity>(1 << 5);
            foreach (var product in _treeValuesCache.GetProducts())
                if (!products.ContainsKey(product.DisplayName) && product.NotificationsSettings is not null)
                    products.Add(product.DisplayName, product.NotificationsSettings);

            var usersChatsCount = 0;
            foreach (var (_, user) in this)
                usersChatsCount += user.Notifications?.Telegram?.Chats?.Count ?? 0;

            foreach (var sensor in _treeValuesCache.GetSensors())
            {
                var sensorPolicies = sensor.Policies.ToList();
                sensorPolicies.Add(sensor.Policies.TimeToLive);

                if (products.TryGetValue(sensor.RootProductName, out var notifications))
                {
                    var chatsCount = (notifications.TelegramSettings?.Chats?.Count ?? 0) + usersChatsCount;

                    foreach (var policy in sensorPolicies)
                        if (policy.Destination is null)
                        {
                            policy.Destination = new();

                            if (notifications.EnabledSensors.Contains(sensor.Id.ToString()))
                            {
                                foreach (var chat in notifications.TelegramSettings.Chats)
                                    if (!notifications.PartiallyIgnored.TryGetValue(chat.Id, out var ignoredSensors) || !ignoredSensors.ContainsKey(sensor.Id.ToString()))
                                        AddChatToDestination(policy, new TelegramChat(chat));
                            }

                            foreach (var (_, user) in this)
                            {
                                if (user.Notifications?.EnabledSensors?.Contains(sensor.Id) ?? false)
                                    foreach (var (_, chat) in user.Notifications.Telegram.Chats)
                                        AddChatToDestination(policy, chat);
                            }

                            if (policy.Destination.Chats.Count == chatsCount)
                            {
                                policy.Destination.AllChats = true;
                                policy.Destination.Chats.Clear();
                            }

                            if (policy is TTLPolicy ttl)
                            {
                                var ttlUpdate = new PolicyUpdate
                                {
                                    Id = ttl.Id,
                                    Conditions = ttl.Conditions.Select(u => new PolicyConditionUpdate(u.Operation, u.Property, u.Target, u.Combination)).ToList(),
                                    Destination = new(ttl.Destination.AllChats, ttl.Destination.Chats.ToDictionary(k => k.Key, v => v.Value)),
                                    Sensitivity = ttl.Sensitivity,
                                    Status = ttl.Status,
                                    Template = ttl.Template,
                                    IsDisabled = ttl.IsDisabled,
                                    Icon = ttl.Icon,
                                };

                                sensor.Policies.UpdateTTL(ttlUpdate);
                                sensorsToResave.Add(sensor.Id);
                            }
                            else
                                policiesToResave[policy.Id] = policy;
                        }
                }
                else
                {
                    foreach (var policy in sensorPolicies)
                    {
                        if (policy.Destination is null)
                        {
                            policy.Destination = new();

                            if (policy is TTLPolicy ttl)
                            {
                                var ttlUpdate = new PolicyUpdate
                                {
                                    Id = ttl.Id,
                                    Conditions = ttl.Conditions.Select(u => new PolicyConditionUpdate(u.Operation, u.Property, u.Target, u.Combination)).ToList(),
                                    Destination = new(ttl.Destination.AllChats, ttl.Destination.Chats.ToDictionary(k => k.Key, v => v.Value)),
                                    Sensitivity = ttl.Sensitivity,
                                    Status = ttl.Status,
                                    Template = ttl.Template,
                                    IsDisabled = ttl.IsDisabled,
                                    Icon = ttl.Icon,
                                };

                                sensor.Policies.UpdateTTL(ttlUpdate);
                                sensorsToResave.Add(sensor.Id);
                            }
                            else
                                policiesToResave[policy.Id] = policy;
                        }
                    }
                }
            }

            foreach (var sensorId in sensorsToResave)
                _treeValuesCache.UpdateSensor(sensorId);

            foreach (var (_, policy) in policiesToResave)
                _treeValuesCache.UpdatePolicy(policy);

            _logger.LogInformation($"{policiesToResave.Count} polices destination migration is finished for {sensorsToResave.Count} sensors");
        }

        [Obsolete("Should be removed after policies chats migration")]
        private static void AddChatToDestination(Policy policy, TelegramChat chat)
        {
            if (!policy.Destination.Chats.ContainsKey(chat.SystemId))
                policy.Destination.Chats.Add(chat.SystemId, chat.Name);
        }
    }
}