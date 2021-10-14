using HSMServer.Core.Model.Authentication;
using System.Collections.Generic;

namespace HSMServer.SignalR
{
    internal class SignalRSessionsManager : ISignalRSessionsManager
    {
        private readonly Dictionary<User, List<string>> _userConnectionIdCross;
        private readonly object _lockObj = new object();
        public SignalRSessionsManager()
        {
            _userConnectionIdCross = new Dictionary<User, List<string>>();
        }

        #region Interface implementation

        public void AddConnection(User user, string id)
        {
            AddConnectionInternal(user, id);
        }

        public void RemoveConnection(User user, string id)
        {
            RemoveConnectionInternal(user, id);
        }

        public Dictionary<User, List<string>> UserConnectionDictionary
        {
            get
            {
                Dictionary<User, List<string>> dictionary;
                lock (_lockObj)
                {
                    dictionary = new Dictionary<User, List<string>>(_userConnectionIdCross);
                }

                return dictionary;
            }
        }

        public int GetConnectionsCount(User user)
        {
            return GetSessionsCountInternal(user);
        }

        #endregion

        #region Private methods

        private void AddConnectionInternal(User user, string connectionId)
        {
            lock (_lockObj)
            {
                if (!_userConnectionIdCross.ContainsKey(user))
                {
                    _userConnectionIdCross[user] = new List<string>();
                }
                _userConnectionIdCross[user].Add(connectionId);
            }
        }

        private void RemoveConnectionInternal(User user, string id)
        {
            lock (_lockObj)
            {
                if (_userConnectionIdCross.ContainsKey(user))
                {
                    _userConnectionIdCross[user].Remove(id);
                    if (_userConnectionIdCross[user].Count == 0)
                    {
                        _userConnectionIdCross.Remove(user);
                    }
                }
            }
        }

        private int GetSessionsCountInternal(User user)
        {
            lock (_lockObj)
            {
                if (_userConnectionIdCross.ContainsKey(user))
                {
                    return _userConnectionIdCross[user].Count;
                }
            }

            return 0;
        }
        #endregion

    }
}
