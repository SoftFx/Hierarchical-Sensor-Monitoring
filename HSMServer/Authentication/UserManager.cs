using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.Authentication
{
    public class UserManager
    {
        #region Private fields

        private readonly List<User> _users;
        private readonly TimeSpan _usersUpdateTimeSpan = TimeSpan.FromSeconds(30);
        private DateTime _lastUsersUpdate = DateTime.MinValue;
        private readonly object _accessLock = new object();

        #endregion

        public UserManager()
        {
            _users = new List<User>();
            CheckUsersUpToDate();
        }

        private List<User> ParseUsersFile()
        {
            return new List<User>();
        }

        private void CheckUsersUpToDate()
        {
            if (DateTime.Now - _lastUsersUpdate > _usersUpdateTimeSpan)
            {
                lock (_accessLock)
                {
                    _users.Clear();
                    _users.AddRange(ParseUsersFile());
                }
            }
        }

        public List<PermissionItem> GetUserPermissions(string userName)
        {
            User correspondingUser = null;
            lock (_accessLock)
            {
                correspondingUser = _users.FirstOrDefault(u => u.UserName == userName);
            }

            return correspondingUser != null ? correspondingUser.UserPermissions : new List<PermissionItem>();
        }

        public User GetUserByCertificateThumbprint(string thumbprint)
        {
            User user = null;
            lock (_accessLock)
            {
                user = _users.FirstOrDefault(u => u.CertificateThumbprint == thumbprint);
            }

            return user;
        }
    }
}
