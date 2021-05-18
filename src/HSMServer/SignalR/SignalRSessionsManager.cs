using System;
using System.Collections.Generic;
using HSMServer.Authentication;

namespace HSMServer.SignalR
{
    internal class SignalRSessionsManager : ISignalRSessionsManager
    {
        private readonly UserManager _userManager;
        private readonly Dictionary<User, string> _userConnectionIdCross;
        private readonly object _lockObj = new object();
        public SignalRSessionsManager(UserManager userManager)
        {
            _userManager = userManager;
            _userConnectionIdCross = new Dictionary<User, string>();
        }

        #region Interface implementation

        public void AddConnection(User user, string id)
        {
            AddConnectionInternal(user, id);
        }

        public string GetConnectionId(User user)
        {
            return GetConnectionIdInternal(user);
        }

        public List<User> GetCurrentUsers()
        {
            return GetUsersInternal();
        }

        public List<string> GetCurrentConnectionIds()
        {
            return GetConnectionIdsInternal();
        }

        public void RemoveConnection(User user)
        {
            RemoveConnectionInternal(user);
        }

        public Dictionary<User, string> UserConnectionDictionary
        {
            get
            {
                Dictionary<User, string> dictionary;
                lock (_lockObj)
                {
                    dictionary = new Dictionary<User, string>(_userConnectionIdCross);
                }

                return dictionary;
            }
        }

        #endregion

        #region Private methods

        private void AddConnectionInternal(User user, string connectionId)
        {
            lock (_lockObj)
            {
                _userConnectionIdCross[user] = connectionId;
            }
        }

        private string GetConnectionIdInternal(User user)
        {
            string result = string.Empty;
            lock (_lockObj)
            {
                try
                {
                    result = _userConnectionIdCross[user];
                }
                catch (Exception e)
                {
                    result = string.Empty;
                }
            }

            return result;
        }

        private List<User> GetUsersInternal()
        {
            List<User> list = new List<User>();
            lock (_lockObj)
            {
                list.AddRange(_userConnectionIdCross.Keys);
            }

            return list;
        }

        private List<string> GetConnectionIdsInternal()
        {
            List<string> result = new List<string>();
            lock (_lockObj)
            {
                result.AddRange(_userConnectionIdCross.Values);
            }

            return result;
        }

        private void RemoveConnectionInternal(User user)
        {
            lock (_lockObj)
            {
                _userConnectionIdCross.Remove(user);
            }
        }
        #endregion

    }
}
