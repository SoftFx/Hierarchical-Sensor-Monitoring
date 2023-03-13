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

        // TODO: wait for async Task
        public void UpdateUser(User user)
        {
            if (ContainsKey(user.Id))
                TryUpdate(user);
            else
                TryAdd(user);
        }

        public async Task RemoveUser(string userName)
        {
            if (TryGetByName(userName, out var user))
                await TryRemove(user);
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

        // TODO remove copy object
        public User GetCopyUser(Guid id) => new(this.GetValueOrDefault(id));

        public User GetUser(Guid id) => this.GetValueOrDefault(id);

        public User GetUserByName(string userName) => base[userName];

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
                await TryAdd(new(entity));

            _logger.LogInformation($"Read users from database, users count = {Count}.");
        }

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

                foreach (var userToEdt in updatedUsers)
                    _databaseCore.UpdateUser(userToEdt.ToEntity());
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

                    _databaseCore.UpdateUser(user.ToEntity());
                }
            }
        }
    }
}