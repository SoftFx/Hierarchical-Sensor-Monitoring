using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using HSMCommon;
using HSMServer.Configuration;
using NLog;

namespace HSMServer.Authentication
{
    public class UserManager
    {
        #region Private fields

        private readonly List<User> _users;
        private readonly Logger _logger;
        private readonly TimeSpan _usersUpdateTimeSpan = TimeSpan.FromSeconds(60);
        private DateTime _lastUsersUpdate = DateTime.MinValue;
        private readonly object _accessLock = new object();
        private readonly CertificateManager _certificateManager;
        private readonly string _usersFileName = "users.xml";
        private readonly string _usersFilePath;

        #endregion

        public UserManager(CertificateManager certificateManager)
        {
            _logger = LogManager.GetCurrentClassLogger();
            _certificateManager = certificateManager;
            _users = new List<User>();
            _usersFilePath = Path.Combine(CertificatesConfig.ConfigFolderPath, _usersFileName);
            if (!File.Exists(_usersFilePath))
            {
                _logger.Info("First launch, users file does not exist");
                AddDefaultUser();
            }
            //else
            //{
            //    CheckUsersUpToDate();
            //}
            
            _logger.Info("UserManager initialized");
        }

        private void AddDefaultUser()
        {
            AddNewUser(CommonConstants.DefaultClientUserName,
                CommonConstants.DefaultClientCertificateThumbprint,
                CommonConstants.DefaultClientCrtCertificateName);
        }

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
                _logger.Error(e, "Failed to parse users file!");
            }

            return users;
        }

        private void CreateDefaultUsersFile()
        {
            User defaultUser = new User()
            {
                CertificateFileName = "default.client.crt", 
                CertificateThumbprint = CommonConstants.DefaultClientCertificateThumbprint,
                UserName = "default.client"
            };
            string content = GetUsersXml(new List<User>() {defaultUser});
            FileManager.SafeCreateFile(_usersFilePath);
            FileManager.SafeWriteText(_usersFilePath, content);
        }

        private User ParseUserNode(XmlNode node)
        {
            User user = new User();
            var nameAttr = node.Attributes?["Name"];
            if (nameAttr != null)
            {
                user.UserName = nameAttr.Value;
            }

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

            user.CertificateThumbprint = _certificateManager.GetCertificateByFileName(user.CertificateFileName)?.Thumbprint;
            return string.IsNullOrEmpty(user.UserName) || string.IsNullOrEmpty(user.CertificateThumbprint) ? null : user;
        }

        private void CheckUsersUpToDate()
        {
            if (DateTime.Now - _lastUsersUpdate <= _usersUpdateTimeSpan) 
                return;

            int count = -1;
            lock (_accessLock)
            {
                _users.Clear();
                _users.AddRange(ParseUsersFile());
                _lastUsersUpdate = DateTime.Now;
                count = _users.Count;
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
                user = _users.FirstOrDefault(u => u.CertificateThumbprint.Equals(thumbprint, StringComparison.OrdinalIgnoreCase));
            }

            return user;
        }

        public void AddNewUser(string userName, string certificateThumbprint, string certificateFileName)
        {
            User user = new User
            {
                CertificateThumbprint = certificateThumbprint, UserName = userName,
                CertificateFileName = certificateFileName
            };
            lock (_accessLock)
            {
                _users.Add(user);
            }

            ThreadPool.QueueUserWorkItem(_ => SaveUsers());
        }

        private void SaveUsers()
        {
            try
            {
                List<User> usersCopy = new List<User>();
                lock (_accessLock)
                {
                    usersCopy.AddRange(_users);
                }

                string xml = GetUsersXml(usersCopy);
                FileManager.SafeDelete(_usersFilePath);
                //using FileStream fs = new FileStream(_usersFilePath, FileMode.OpenOrCreate);
                //fs.Write(Encoding.UTF8.GetBytes(xml));
                FileManager.SafeWriteToNewFile(_usersFilePath, xml);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to save users file!");   
            }
        }

        private string GetUsersXml(List<User> users)
        {
            XmlDocument document = new XmlDocument();
            XmlElement rootElement = document.CreateElement("users");
            document.AppendChild(rootElement);
            foreach (var user in users)
            {
                XmlNode node = UserToXml(document, user);
                rootElement.AppendChild(node);
            }

            StringBuilder sb = new StringBuilder();
            using (StringWriter sw = new StringWriter(sb))
            {
                document.Save(sw);
            }

            sb.Replace("encoding=\"utf-16\"", string.Empty);

            return sb.ToString();
        }

        private XmlElement UserToXml(XmlDocument document, User user)
        {
            XmlElement rootElement = document.CreateElement("user");

            XmlAttribute nameAttr = document.CreateAttribute("Name");
            nameAttr.Value = user.UserName;
            rootElement.Attributes.Append(nameAttr);

            XmlAttribute certificateAttr = document.CreateAttribute("Certificate");
            certificateAttr.Value = user.CertificateFileName;
            rootElement.Attributes.Append(certificateAttr);

            //TODO: serialize products when they will be needed
            //if (user.UserPermissions.Count > 0)
            //{
            //    rootElement.AppendChild(UserPermissionsToXml(document, user.UserPermissions));
            //}

            return rootElement;
        }

        private XmlElement UserPermissionsToXml(XmlDocument document, List<PermissionItem> items)
        {
            XmlElement productsElement = document.CreateElement("products");

            foreach (var item in items)
            {
                XmlElement element = document.CreateElement("product");
                productsElement.AppendChild(element);

                XmlAttribute nameAttr = document.CreateAttribute("Name");
                nameAttr.Value = item.ProductName;
                //TODO: add ignored sensors specification
            }

            return productsElement;
        }
    }
}
