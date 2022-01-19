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
        private readonly CertificateManager _certificateManager;
        private readonly IDatabaseAdapter _databaseAdapter;
        private readonly string _usersFileName = "users.xml";
        private readonly string _usersFilePath;

        #endregion

        public UserManager(CertificateManager certificateManager, IDatabaseAdapter databaseAdapter, ILogger<UserManager> logger)
        {
            _logger = logger;
            _certificateManager = certificateManager;
            _users = new List<User>();
            _databaseAdapter = databaseAdapter;
            _usersFilePath = Path.Combine(CertificatesConfig.ConfigFolderPath, _usersFileName);
            List<User> dataBaseUsers = ReadUserFromDatabase();
            if (File.Exists(_usersFilePath))
            {
                Thread.Sleep(300); 
                MigrateUsersToDatabase();
                File.Delete(_usersFilePath);
                _logger.LogInformation("Users file deleted");
            }

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

        public void AddUser(User user)
        {
            lock (_accessLock)
            {
                _users.Add(user);
            }

            Task.Run(() => _databaseAdapter.AddUser(user));
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

        private void MigrateUsersToDatabase()
        {
            List<User> usersFromFile = ParseUsersFile();
            foreach (var user in usersFromFile)
            {
                if (user.UserName.Equals("default"))
                    user.IsAdmin = true;

                if (string.IsNullOrEmpty(user.Password))
                {
                    AddUser(user.UserName, user.CertificateThumbprint, user.CertificateFileName, 
                        HashComputer.ComputePasswordHash(user.UserName), user.IsAdmin);
                }
            }

            _logger.LogInformation($"{usersFromFile.Count} successfully migrated from file to database");
        }

        #region File work

        [Obsolete]
        private List<User> ParseUsersFile()
        {
            List<User> users = new List<User>();
            try
            {
                XmlDocument document = new XmlDocument();
                document.Load(_usersFilePath);

                XmlNodeList nodes = document.SelectNodes("//users/user");

                if (nodes == null)
                    return users;

                foreach (XmlNode node in nodes)
                {
                    var user = ParseUserNode(node);
                    if (user != null)
                    {
                        users.Add(user);
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to parse users file!");
            }

            return users;
        }

        [Obsolete]
        private User ParseUserNode(XmlNode node)
        {
            User user;
            var nameAttr = node.Attributes?["Name"];
            if (nameAttr == null)
                return null;

            user = new User(nameAttr.Value);
            var certAttr = node.Attributes?["Certificate"];
            if (certAttr != null)
            {
                user.CertificateFileName = certAttr.Value;
            }

            XmlNodeList products = node.SelectNodes("//products/product");
            if (products != null)
            {
                foreach (XmlNode serverNode in products)
                {
                    PermissionItem permissionItem = new PermissionItem();
                    var serverNodeAttr = serverNode.Attributes?["Name"];
                    if (serverNodeAttr != null)
                    {
                        permissionItem.ProductName = serverNodeAttr.Value;
                    }
                }
            }

            user.CertificateThumbprint = _certificateManager.GetCertificateByFileName(user.CertificateFileName)?.Thumbprint;
            return string.IsNullOrEmpty(user.UserName) || string.IsNullOrEmpty(user.CertificateThumbprint) ? null : user;
        }

        #endregion

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
