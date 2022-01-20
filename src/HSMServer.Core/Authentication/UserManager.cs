using HSMCommon;
using HSMCommon.Constants;
using HSMServer.Core.Authentication.UserObserver;
using HSMServer.Core.Configuration;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Extensions;
using HSMServer.Core.Helpers;
using HSMServer.Core.Model.Authentication;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace HSMServer.Core.Authentication
{
    public class UserManager : UserObservableImpl, IUserManager
    {
        #region Private fields

        private readonly List<User> _users;
        private readonly ILogger<UserManager> _logger;
        private readonly TimeSpan _usersUpdateTimeSpan = TimeSpan.FromSeconds(60);
        private DateTime _lastUsersUpdate = DateTime.MinValue;
        private readonly object _accessLock = new object();
        private readonly IDatabaseAdapter _databaseAdapter;

        #endregion

        public UserManager(IDatabaseAdapter databaseAdapter, ILogger<UserManager> logger)
        {
            _logger = logger;
            _users = new List<User>();
            _databaseAdapter = databaseAdapter;
            List<User> dataBaseUsers = ReadUserFromDatabase();

            int count = dataBaseUsers.Count;
            lock (_accessLock)
            {
                count += _users.Count;
            }

            if (count == 0)
            {
                AddDefaultUser();
                _logger.LogInformation("Default user added");
            }

            CheckUsersUpToDate();

            _logger.LogInformation("UserManager initialized");
        }

        #region Interface implementation

        public User GetUserByCertificateThumbprint(string thumbprint)
        {
            CheckUsersUpToDate();
            User user = null;
            lock (_accessLock)
            {
                user = _users.FirstOrDefault(u => u.CertificateThumbprint != null 
                    && u.CertificateThumbprint.Equals(thumbprint, StringComparison.InvariantCultureIgnoreCase));
            }

            return user;
        }

        public void RemoveUser(User user)
        {
            lock (_accessLock)
            {
                var existingUser = _users.First(x => x.Id == user.Id);
                _users.Remove(existingUser);

                Task.Run(() => _databaseAdapter.RemoveUser(existingUser));
            }
        }

        public void RemoveUser(string userName)
        {
            User correspondingUser = default(User);
            lock (_accessLock)
            {
                correspondingUser = _users.FirstOrDefault(u => u.UserName == userName);
            }

            if (correspondingUser != null)
            {
                RemoveUser(correspondingUser);
            }
        }

        private void AddUser(User user)
        {
            lock (_accessLock)
            {
                _users.Add(user);
            }

            //Task.Run(() => _databaseAdapter.AddUser(user));
            _databaseAdapter.AddUser(user);
        }

        public void AddUser(string userName, string certificateThumbprint, string certificateFileName,
            string passwordHash, bool isAdmin, List<KeyValuePair<string, ProductRoleEnum>> productRoles = null)
        {
            User user = new User (userName)
            {
                CertificateThumbprint = certificateThumbprint,
                CertificateFileName = certificateFileName,
                Password = passwordHash,
                IsAdmin = isAdmin
            };

            if (productRoles != null && productRoles.Any())
            {
                user.ProductsRoles = productRoles;
            }

            AddUser(user);
        }

        public List<User> Users
        {
            get
            {
                CheckUsersUpToDate();
                List<User> users = new List<User>();
                lock (_accessLock)
                {
                    users.AddRange(_users);
                }

                return users;
            }
        }

        public User GetUser(Guid id)
        {
            User result = default(User);
            lock (_accessLock)
            {
                result = _users.FirstOrDefault(u => u.Id == id);
            }
            return new User(result);
        }

        public User GetUserByUserName(string username)
        {
            User result = default(User);
            lock (_accessLock)
            {
                result = _users.FirstOrDefault(u => u.UserName == username);
            }

            return result == null ? result : new User(result);
        }

        public List<User> GetViewers(string productKey)
        {
            if (_users == null || !_users.Any()) return null;

            List<User> result = new List<User>();
            foreach (var user in _users)
            {
                var pair = user.ProductsRoles?.FirstOrDefault(x => x.Key.Equals(productKey));
                if (pair.Value.Key != null)
                    result.Add(user);
            }

            return result;
        }

        public List<User> GetManagers(string productKey)
        {
            if (_users == null || !_users.Any()) return null;

            List<User> result = new List<User>();
            foreach (var user in _users)
            {
                if (ProductRoleHelper.IsManager(productKey, user.ProductsRoles))
                    result.Add(user);
            }

            return result;
        }

        public List<User> GetUsersNotAdmin()
        {
            if (_users == null || !_users.Any()) return null;

            List<User> result = new List<User>();
            foreach(var user in _users)
            {
                if (user.IsAdmin) continue;
                result.Add(user);
            }

            return result;
        }

        #endregion

        private void CheckUsersUpToDate()
        {
            if (DateTime.Now - _lastUsersUpdate <= _usersUpdateTimeSpan)
                return;

            int count = -1;
            lock (_accessLock)
            {
                _users.Clear();
                _users.AddRange(ReadUserFromDatabase());
                _lastUsersUpdate = DateTime.Now;
                count = _users.Count;
            }

            _logger.LogInformation($"Users read, users count = {count}");
        }

        private void AddDefaultUser()
        {
            AddUser(CommonConstants.DefaultUserUsername,
                CommonConstants.DefaultClientCertificateThumbprint,
                CommonConstants.DefaultClientCrtCertificateName,
                HashComputer.ComputePasswordHash(CommonConstants.DefaultUserUsername), true);
        }

        private List<User> ReadUserFromDatabase()
        {
            return _databaseAdapter.GetUsers();
        }

        public User Authenticate(string login, string password)
        {
            var passwordHash = HashComputer.ComputePasswordHash(password);
            var existingUser = Users.SingleOrDefault(u => u.UserName.Equals(login) && !string.IsNullOrEmpty(u.Password) && u.Password.Equals(passwordHash));

            return existingUser?.WithoutPassword();
        }

        public void UpdateUser(User user)
        {
            User existingUser;
            lock (_accessLock)
            {
                existingUser = _users.FirstOrDefault(u => u.Id == user.Id);
            }

            if (existingUser == null)
            {
                AddUser(user);
                return;
            }

            existingUser.Update(user);
            FireUserChanged(existingUser);
            Task.Run(() => _databaseAdapter.UpdateUser(existingUser));
        }
    }
}
