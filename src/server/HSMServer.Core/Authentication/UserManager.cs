using HSMCommon;
using HSMCommon.Constants;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Extensions;
using HSMServer.Core.Helpers;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.Core.Authentication
{
    public partial class UserManager : IUserManager
    {
        private readonly ConcurrentDictionary<Guid, User> _users;
        private readonly ConcurrentDictionary<string, Guid> _userNames;

        private readonly IDatabaseCore _databaseCore;
        private readonly ILogger<UserManager> _logger;

        private readonly AddUserActionHandler _addUserActionHandler;
        private readonly RemoveUserActionHandler _removeUserActionHandler;
        private readonly UpdateUserActionHandler _updateUserActionHandler;

        public event Action<User> UpdateUserEvent;


        public UserManager(IDatabaseCore databaseCore, ILogger<UserManager> logger)
        {
            _databaseCore = databaseCore;
            _logger = logger;

            _users = new ConcurrentDictionary<Guid, User>();
            _userNames = new ConcurrentDictionary<string, Guid>();

            _addUserActionHandler = new AddUserActionHandler(this);
            _removeUserActionHandler = new RemoveUserActionHandler(this);
            _updateUserActionHandler = new UpdateUserActionHandler(this);

            InitializeUsers();

            _logger.LogInformation("UserManager initialized");
        }


        public void AddUser(string userName, string certificateThumbprint, string certificateFileName,
            string passwordHash, bool isAdmin, List<KeyValuePair<string, ProductRoleEnum>> productRoles = null)
        {
            User user = new(userName)
            {
                CertificateThumbprint = certificateThumbprint,
                CertificateFileName = certificateFileName,
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

        public Task UpdateTelegramSettings(Guid userId, TelegramSettings settings)
        {
            var user = GetUser(userId);
            user.NotificationSettings.TelegramSettings = settings;

            return _updateUserActionHandler.Apply(user);
        }

        // TODO: wait for async Task
        public async void RemoveUser(string userName)
        {
            if (_userNames.TryGetValue(userName, out var userId) && _users.TryGetValue(userId, out var user))
                await _removeUserActionHandler.Apply(user);
            else
                _logger.LogWarning($"There are no users with name={userName} to remove");
        }

        public void RemoveProductFromUsers(string productKey)
        {
            var updatedUsers = new List<User>(1 << 2);

            foreach (var user in _users)
            {
                var removedRolesCount = user.Value.ProductsRoles.RemoveAll(role => role.Key == productKey);
                if (removedRolesCount == 0)
                    continue;

                updatedUsers.Add(user.Value);
            }

            foreach (var userToEdt in updatedUsers)
                _databaseCore.UpdateUser(userToEdt);
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
        public User GetUser(Guid id) => new(_users.GetValueOrDefault(id));

        public User GetUserByUserName(string userName) =>
            !string.IsNullOrEmpty(userName) && _userNames.TryGetValue(userName, out var userId) && _users.TryGetValue(userId, out var user)
                ? new User(user)
                : null;

        public List<User> GetViewers(string productKey)
        {
            var result = new List<User>(1 << 2);

            if (_users.IsEmpty)
                return result;

            foreach (var user in _users)
            {
                var pair = user.Value.ProductsRoles?.FirstOrDefault(r => r.Key.Equals(productKey));
                if (pair != null && pair.Value.Key != null)
                    result.Add(user.Value);
            }

            return result;
        }

        public List<User> GetManagers(string productKey)
        {
            var result = new List<User>(1 << 2);

            if (_users.IsEmpty)
                return result;

            foreach (var user in _users)
                if (ProductRoleHelper.IsManager(productKey, user.Value.ProductsRoles))
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
            var usersFromDB = _databaseCore.GetUsers();

            if (usersFromDB.Count == 0)
            {
                AddDefaultUser();
                _logger.LogInformation("Default user has been added.");
            }

            foreach (var user in usersFromDB)
                await _addUserActionHandler.Apply(user, false);

            _logger.LogInformation($"Read users from database, users count = {_users.Count}.");
        }

        private void AddDefaultUser() =>
            AddUser(CommonConstants.DefaultUserUsername,
                    CommonConstants.DefaultClientCertificateThumbprint,
                    CommonConstants.DefaultClientCrtCertificateName,
                    HashComputer.ComputePasswordHash(CommonConstants.DefaultUserUsername),
                    true);
    }
}