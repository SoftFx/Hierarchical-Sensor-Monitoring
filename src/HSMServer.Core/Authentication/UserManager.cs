using HSMCommon;
using HSMCommon.Constants;
using HSMServer.Core.Authentication.UserObserver;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Extensions;
using HSMServer.Core.Helpers;
using HSMServer.Core.Model.Authentication;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.Core.Authentication
{
    public class UserManager : UserObservableImpl, IUserManager
    {
        private const int MaxAttemptsCount = 3;

        private readonly ConcurrentDictionary<Guid, User> _users;
        private readonly ConcurrentDictionary<string, Guid> _userNames;

        private readonly IDatabaseAdapter _databaseAdapter;
        private readonly ILogger<UserManager> _logger;

        public ICollection<User> Users => _users.Values;


        public UserManager(IDatabaseAdapter databaseAdapter, ILogger<UserManager> logger)
        {
            _databaseAdapter = databaseAdapter;
            _logger = logger;

            _users = new ConcurrentDictionary<Guid, User>();
            _userNames = new ConcurrentDictionary<string, Guid>();

            InitializeUsers();

            _logger.LogInformation("UserManager initialized");
        }


        public void AddUser(string userName, string certificateThumbprint, string certificateFileName,
            string passwordHash, bool isAdmin, List<KeyValuePair<string, ProductRoleEnum>> productRoles = null) =>
            AddUser(
                new(userName)
                {
                    CertificateThumbprint = certificateThumbprint,
                    CertificateFileName = certificateFileName,
                    Password = passwordHash,
                    IsAdmin = isAdmin,
                    ProductsRoles = (productRoles?.Count ?? 0) > 0 ? productRoles : null,
                });

        public void UpdateUser(User user)
        {
            if (!_users.TryGetValue(user.Id, out var existingUser))
            {
                AddUser(user);
                return;
            }

            existingUser.Update(user);
            FireUserChanged(existingUser);
            _databaseAdapter.UpdateUser(existingUser);
        }

        public void RemoveUser(string userName)
        {
            if (!_userNames.TryGetValue(userName, out var userId) ||
                !_users.TryGetValue(userId, out var user))
            {
                _logger.LogWarning($"There are no users with name={userName} to remove");
                return;
            }

            TryUserAction(() => _users.TryRemove(user.Id, out var _),
                          () => _userNames.TryRemove(user.UserName, out var _),
                          () => _databaseAdapter.RemoveUser(user));
        }

        public User Authenticate(string login, string password)
        {
            var passwordHash = HashComputer.ComputePasswordHash(password);
            var existingUser = Users.SingleOrDefault(u => u.UserName.Equals(login) && !string.IsNullOrEmpty(u.Password) && u.Password.Equals(passwordHash));

            return existingUser?.WithoutPassword();
        }


        public User GetUser(Guid id) => new(_users.TryGetValue(id, out var user) ? user : null);

        public User GetUserByUserName(string userName)
        {
            if (!_userNames.TryGetValue(userName, out var userId) ||
                !_users.TryGetValue(userId, out var user))
                return null;

            return new User(user);
        }

        public List<User> GetViewers(string productKey)
        {
            var result = new List<User>();

            if ((_users?.Count ?? 0) == 0)
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
            var result = new List<User>();

            if ((_users?.Count ?? 0) == 0)
                return result;

            foreach (var user in _users)
                if (ProductRoleHelper.IsManager(productKey, user.Value.ProductsRoles))
                    result.Add(user.Value);

            return result;
        }

        public List<User> GetUsersNotAdmin()
        {
            if ((_users?.Count ?? 0) == 0)
                return null;

            var result = new List<User>();

            foreach (var user in _users)
                if (!user.Value.IsAdmin)
                    result.Add(user.Value);

            return result;
        }

        private void AddUser(User user) =>
            TryUserAction(() => _users.TryAdd(user.Id, user),
                          () => _userNames.TryAdd(user.UserName, user.Id),
                          () => _databaseAdapter.AddUser(user));

        private void InitializeUsers()
        {
            var usersFromDB = _databaseAdapter.GetUsers();

            if (usersFromDB.Count == 0)
            {
                AddDefaultUser();
                _logger.LogInformation("Added default user.");
            }

            foreach (var user in usersFromDB)
                TryUserAction(() => _users.TryAdd(user.Id, user),
                              () => _userNames.TryAdd(user.UserName, user.Id));

            _logger.LogInformation($"Read users from database, users count = {_users.Count}.");
        }

        private void AddDefaultUser() =>
            AddUser(CommonConstants.DefaultUserUsername,
                    CommonConstants.DefaultClientCertificateThumbprint,
                    CommonConstants.DefaultClientCrtCertificateName,
                    HashComputer.ComputePasswordHash(CommonConstants.DefaultUserUsername),
                    true);

        private static async void TryUserAction(Func<bool> tryUsersFunc, Func<bool> tryUserNamesFunc, Action dbAction = null)
        {
            int count = 0;
            while (count < MaxAttemptsCount)
            {
                ++count;

                if (!tryUsersFunc())
                {
                    await Task.Yield();
                    continue;
                }

                tryUserNamesFunc();
                dbAction?.Invoke();
            }
        }
    }
}
