using HSMCommon;
using HSMCommon.Constants;
using HSMServer.Core.Cache;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model;
using HSMServer.Extensions;
using HSMServer.Helpers;
using HSMServer.Model.Authentication;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Authentication
{
    public partial class UserManager : IUserManager
    {
        private readonly ConcurrentDictionary<Guid, User> _users;
        private readonly ConcurrentDictionary<string, Guid> _userNames;

        private readonly IDatabaseCore _databaseCore;
        private readonly ITreeValuesCache _treeValuesCache;
        private readonly ILogger<UserManager> _logger;

        private readonly AddUserActionHandler _addUserActionHandler;
        private readonly RemoveUserActionHandler _removeUserActionHandler;
        private readonly UpdateUserActionHandler _updateUserActionHandler;

        public event Action<User> UpdateUserEvent;
        public event Action<User> RemoveUserEvent;


        public UserManager(IDatabaseCore databaseCore, ITreeValuesCache cache, ILogger<UserManager> logger)
        {
            _databaseCore = databaseCore;
            _logger = logger;

            _treeValuesCache = cache;
            _treeValuesCache.ChangeProductEvent += ChangeProductEventHandler;
            _treeValuesCache.ChangeSensorEvent += ChangeSensorEventHandler;

            _users = new ConcurrentDictionary<Guid, User>();
            _userNames = new ConcurrentDictionary<string, Guid>();

            _addUserActionHandler = new AddUserActionHandler(this);
            _removeUserActionHandler = new RemoveUserActionHandler(this);
            _updateUserActionHandler = new UpdateUserActionHandler(this);

            InitializeUsers();

            _logger.LogInformation("UserManager initialized");
        }

        public void AddUser(string userName, string passwordHash, bool isAdmin, List<(Guid, ProductRoleEnum)> productRoles = null)
        {
            User user = new(userName)
            {
                Password = passwordHash,
                IsAdmin = isAdmin,
            };

            if (productRoles != null && productRoles.Count > 0)
                user.ProductsRoles = productRoles;

            AddUser(user);
        }

        // TODO: wait for async Task
        public async void AddUser(User user) =>
            await _addUserActionHandler.Apply(user);

        // TODO: wait for async Task
        public async void UpdateUser(User user)
        {
            if (_users.ContainsKey(user.Id))
                await _updateUserActionHandler.Apply(user);
            else
                AddUser(user);
        }

        // TODO: wait for async Task
        public async void RemoveUser(string userName)
        {
            if (_userNames.TryGetValue(userName, out var userId) && _users.TryGetValue(userId, out var user))
                await _removeUserActionHandler.Apply(user);
            else
                _logger.LogWarning($"There are no users with name={userName} to remove");
        }

        public User Authenticate(string login, string password)
        {
            var passwordHash = HashComputer.ComputePasswordHash(password);

            bool IsAskedUser(KeyValuePair<Guid, User> userPair)
            {
                var user = userPair.Value;
                return user.UserName.Equals(login) && !string.IsNullOrEmpty(user.Password) && user.Password.Equals(passwordHash);
            }

            var existingUser = _users.SingleOrDefault(IsAskedUser);

            return existingUser.Value?.WithoutPassword();
        }

        // TODO remove copy object
        public User GetCopyUser(Guid id) => new(_users.GetValueOrDefault(id));

        public User GetUser(Guid id) => _users.GetValueOrDefault(id);

        public User GetUserByUserName(string userName) =>
            !string.IsNullOrEmpty(userName) && _userNames.TryGetValue(userName, out var userId) && _users.TryGetValue(userId, out var user)
                ? new User(user)
                : null;

        public List<User> GetViewers(Guid productId)
        {
            var result = new List<User>(1 << 2);

            if (_users.IsEmpty)
                return result;

            foreach (var user in _users)
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

            if (_users.IsEmpty)
                return result;

            foreach (var user in _users)
                if (ProductRoleHelper.IsManager(productId, user.Value.ProductsRoles))
                    result.Add(user.Value);

            return result;
        }

        public IEnumerable<User> GetUsers(Func<User, bool> filter = null)
        {
            var users = _users.Values;

            return filter != null ? users.Where(filter) : users;
        }

        private async void InitializeUsers()
        {
            var userEntities = _databaseCore.GetUsers();

            if (userEntities.Count == 0)
            {
                AddDefaultUser();
                _logger.LogInformation("Default user has been added.");
            }

            foreach (var entity in userEntities)
                await _addUserActionHandler.Apply(new(entity), false);

            _logger.LogInformation($"Read users from database, users count = {_users.Count}.");
        }

        private void AddDefaultUser() =>
            AddUser(CommonConstants.DefaultUserUsername,
                    HashComputer.ComputePasswordHash(CommonConstants.DefaultUserUsername),
                    true);

        private void ChangeProductEventHandler(ProductModel product, ActionType transaction)
        {
            if (transaction == ActionType.Delete)
            {
                var updatedUsers = new List<User>(1 << 2);

                foreach (var user in _users)
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

        private void ChangeSensorEventHandler(BaseSensorModel sensor, ActionType transaction)
        {
            if (transaction == ActionType.Delete)
            {
                foreach (var (_, user) in _users)
                {
                    if (!user.Notifications.RemoveSensor(sensor.Id))
                        continue;

                    _databaseCore.UpdateUser(user.ToEntity());
                }
            }
        }
    }
}