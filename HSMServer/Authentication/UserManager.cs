using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using HSMServer.Configuration;
using NLog;

namespace HSMServer.Authentication
{
    public class UserManager
    {
        #region Private fields

        private readonly List<User> _users;
        private readonly Logger _logger;
        private readonly TimeSpan _usersUpdateTimeSpan = TimeSpan.FromSeconds(30);
        private DateTime _lastUsersUpdate = DateTime.MinValue;
        private readonly object _accessLock = new object();
        private readonly CertificateManager _certificateManager;
        private readonly string _usersFileName = "users.xml";

        #endregion

        public UserManager(CertificateManager certificateManager)
        {
            _logger = LogManager.GetCurrentClassLogger();
            _certificateManager = certificateManager;
            _users = new List<User>();
            CheckUsersUpToDate();
            _logger.Info("UserManager initialized");
        }

        private List<User> ParseUsersFile()
        {
            string usersFilePath = Path.Combine(Config.ConfigFolderPath, _usersFileName);

            XmlDocument document = new XmlDocument();
            document.Load(usersFilePath);
            
            XmlNodeList nodes = document.SelectNodes("//users/user");

            if (nodes == null)
                return new List<User>();

            List<User> users = new List<User>();
            foreach (XmlNode node in nodes)
            {
                var user = ParseUserNode(node);
                if (user != null)
                {
                    users.Add(user);
                }
            }

            return users;
        }

        private User ParseUserNode(XmlNode node)
        {
            User user = new User();
            var nameAttr = node.Attributes?["Name"];
            if (nameAttr != null)
            {
                user.UserName = nameAttr.Value;
            }

            string certFile = string.Empty;
            var certAttr = node.Attributes?["Certificate"];
            if (certAttr != null)
            {
                certFile = certAttr.Value;
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

                    //temporarily disable ignore 
                    //var ignoredSensorsAttr = serverNode.Attributes?["ignoredSensors"];
                    //if (ignoredSensorsAttr != null)
                    //{
                    //    permissionItem.IgnoredSensors = ignoredSensorsAttr.Value.Split(new[] {';'}).ToList();
                    //}

                    if (!string.IsNullOrEmpty(permissionItem.ProductName))
                    {
                        user.UserPermissions.Add(permissionItem);
                    }
                }
            }

            user.CertificateThumbprint = _certificateManager.GetCertificateByFileName(certFile)?.Thumbprint;
            return string.IsNullOrEmpty(user.UserName) || string.IsNullOrEmpty(user.CertificateThumbprint) ? null : user;
        }

        private void CheckUsersUpToDate()
        {
            int count = -1;
            if (DateTime.Now - _lastUsersUpdate > _usersUpdateTimeSpan)
            {
                lock (_accessLock)
                {
                    _users.Clear();
                    _users.AddRange(ParseUsersFile());
                    _lastUsersUpdate = DateTime.Now;
                    count = _users.Count;
                }
            }
            _logger.Info($"Users read, users count = {count}");
        }

        public List<PermissionItem> GetUserPermissions(string userName)
        {
            CheckUsersUpToDate();
            User correspondingUser = null;
            lock (_accessLock)
            {
                correspondingUser = _users.FirstOrDefault(u => u.UserName == userName);
            }

            return correspondingUser != null ? correspondingUser.UserPermissions : new List<PermissionItem>();
        }

        public User GetUserByCertificateThumbprint(string thumbprint)
        {
            CheckUsersUpToDate();
            User user = null;
            lock (_accessLock)
            {
                user = _users.FirstOrDefault(u => u.CertificateThumbprint == thumbprint);
            }

            return user;
        }
    }
}
