using HSMCommon;
using HSMCommon.Constants;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.Cache;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model;
using HSMServer.Extensions;
using HSMServer.Helpers;
using HSMServer.Model.Authentication;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.Authentication
{
    public sealed class UserManager : ConcurrentStorage<User, UserEntity, UserUpdate>, IUserManager
    {
        private readonly IDatabaseCore _databaseCore;
        private readonly ITreeValuesCache _treeValuesCache;
        private readonly ILogger<UserManager> _logger;


        protected override Action<UserEntity> AddToDb => _databaseCore.AddUser;

        protected override Action<UserEntity> UpdateInDb => _databaseCore.UpdateUser;

        protected override Action<User> RemoveFromDb => user => _databaseCore.RemoveUser(user.ToEntity());


        public UserManager(IDatabaseCore databaseCore, ITreeValuesCache cache, ILogger<UserManager> logger)
        {
            _databaseCore = databaseCore;
            _logger = logger;

            _treeValuesCache = cache;
            _treeValuesCache.ChangeProductEvent += ChangeProductEventHandler;
            _treeValuesCache.ChangeSensorEvent += ChangeSensorEventHandler;
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

        public User Authenticate(string login, string password)
        {
            var passwordHash = HashComputer.ComputePasswordHash(password);

            bool IsAskedUser(KeyValuePair<Guid, User> userPair)
            {
                var user = userPair.Value;
                return user.Name.Equals(login) && !string.IsNullOrEmpty(user.Password) && user.Password.Equals(passwordHash);
            }

            var existingUser = this.SingleOrDefault(IsAskedUser);

            return existingUser.Value?.WithoutPassword();
        }

        public List<User> GetViewers(Guid productId)
        {
            var result = new List<User>(1 << 2);

            if (IsEmpty)
                return result;

            foreach (var user in this)
            {
                var pair = user.Value.ProductsRoles?.FirstOrDefault(r => r.Item1.Equals(productId));
                if (pair != null && pair.Value.Item1 != Guid.Empty)
                    result.Add(user.Value);
            }

            return result;
        }

        public List<User> GetManagers(Guid productId)
        {
            var result = new List<User>(1 << 2);

            if (IsEmpty)
                return result;

            foreach (var user in this)
                if (ProductRoleHelper.IsManager(productId, user.Value.ProductsRoles))
                    result.Add(user.Value);

            return result;
        }

        public IEnumerable<User> GetUsers(Func<User, bool> filter = null) => filter != null ? Values.Where(filter) : Values;

        public async Task InitializeUsers()
        {
            var userEntities = _databaseCore.GetUsers();

            if (userEntities.Count == 0)
            {
                await AddDefaultUser();
                _logger.LogInformation("Default user has been added.");
            }

            foreach (var entity in userEntities)
                TryAdd(entity);

            _logger.LogInformation($"Read users from database, users count = {Count}.");
        }

        protected override User FromEntity(UserEntity entity) => new(entity);

        private Task<bool> AddDefaultUser() =>
            AddUser(CommonConstants.DefaultUserUsername,
                    HashComputer.ComputePasswordHash(CommonConstants.DefaultUserUsername),
                    true);

        private void ChangeProductEventHandler(ProductModel product, TransactionType transaction)
        {
            if (transaction == TransactionType.Delete)
            {
                var updatedUsers = new List<User>(1 << 2);

                foreach (var user in this)
                {
                    var removedRolesCount = user.Value.ProductsRoles.RemoveAll(role => role.Item1 == product.Id);
                    if (removedRolesCount == 0)
                        continue;

                    updatedUsers.Add(user.Value);
                }

                foreach (var userToEdit in updatedUsers)
                    TryUpdate(userToEdit);
            }
        }

        private void ChangeSensorEventHandler(BaseSensorModel sensor, TransactionType transaction)
        {
            if (transaction == TransactionType.Delete)
            {
                foreach (var (_, user) in this)
                {
                    if (!user.Notifications.RemoveSensor(sensor.Id))
                        continue;

                    TryUpdate(user);
                }
            }
        }
    }
}