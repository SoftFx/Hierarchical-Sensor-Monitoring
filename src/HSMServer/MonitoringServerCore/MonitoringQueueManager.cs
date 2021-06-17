using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using HSMCommon.Model.SensorsData;
using HSMServer.Authentication;
using HSMServer.Extensions;
using NLog;

namespace HSMServer.MonitoringServerCore
{
    internal class MonitoringQueueManager : IMonitoringQueueManager, IDisposable
    {
        #region IDisposable implementation

        private bool _disposed;

        // Implement IDisposable.
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposingManagedResources)
        {
            // The idea here is that Dispose(Boolean) knows whether it is 
            // being called to do explicit cleanup (the Boolean is true) 
            // versus being called due to a garbage collection (the Boolean 
            // is false). This distinction is useful because, when being 
            // disposed explicitly, the Dispose(Boolean) method can safely 
            // execute code using reference type fields that refer to other 
            // objects knowing for sure that these other objects have not been 
            // finalized or disposed of yet. When the Boolean is false, 
            // the Dispose(Boolean) method should not execute code that 
            // refer to reference type fields because those objects may 
            // have already been finalized."

            if (!_disposed)
            {
                if (disposingManagedResources)
                {
                    lock (_accessLock)
                    {
                        foreach (var sessionPair in _currentSessions)
                        {
                            sessionPair.Value.Clear();
                            _currentSessions.Remove(sessionPair.Key);
                        }
                    }   
                }

                // Mark as disposed.
                _disposed = true;
            }
        }

        // Use C# destructor syntax for finalization code.
        ~MonitoringQueueManager()
        {
            // Simply call Dispose(false).
            Dispose(false);
        }

        #endregion

        private readonly Dictionary<User, ClientMonitoringQueue> _currentSessions;
        private readonly Logger _logger;
        private readonly object _accessLock = new object();
        public MonitoringQueueManager()
        {
            _logger = LogManager.GetCurrentClassLogger();
            lock (_accessLock)
            {
                _currentSessions = new Dictionary<User, ClientMonitoringQueue>();
            }
            
            _logger.Info("Monitoring queue manager initialized");
        }

        #region Interface implementation

        public bool IsUserRegistered(User user)
        {
            bool isRegistered;
            lock (_accessLock)
            {
                var correspondingUser = _currentSessions.Keys.FirstOrDefault(u =>
                    u.IsSame(user));
                isRegistered = correspondingUser != null;
            }

            return isRegistered;
        }

        public void AddUserSession(User user, IPAddress address, int port)
        {
            AddUserSession(user);
        }
        public void AddUserSession(User user)
        {
            lock (_accessLock)
            {
                //TODO: analyze the process, maybe will need to do nothing if the User queue already exists
                //if (_currentSessions.ContainsKey(user))
                //{
                //    _currentSessions[user].Clear();
                //    _currentSessions.Remove(user);
                //}

                if (!_currentSessions.ContainsKey(user))
                {
                    ClientMonitoringQueue queue = new ClientMonitoringQueue(user.UserName);
                    queue.QueueOverflow += QueueOverflow;
                    //queue.UserDisconnected += Queue_UserDisconnected;
                    _currentSessions[user] = queue;
                }
            }
        }

        private void Queue_UserDisconnected(object sender, string e)
        {
            RemoveQueue(e);
        }

        private void QueueOverflow(object sender, ClientMonitoringQueue e)
        {
            lock (_accessLock)
            {
                var correspondingUser = _currentSessions.Keys.FirstOrDefault(u => u.UserName.Equals(e.GetUserName()));
                if (correspondingUser != null)
                {
                    RemoveUserSession(correspondingUser);
                }
            }
        }

        private void RemoveQueue(string userName)
        {
            lock (_accessLock)
            {
                var pair = _currentSessions.FirstOrDefault(p => p.Key.UserName == userName);
                pair.Value.Clear();
                pair.Value.UserDisconnected -= Queue_UserDisconnected;
                pair.Value.QueueOverflow -= QueueOverflow;
                _currentSessions.Remove(pair.Key);
            }
        }
        public void RemoveUserSession(User user)
        {
            lock (_accessLock)
            {
                if (_currentSessions.ContainsKey(user))
                {
                    _currentSessions[user].Clear();
                    _currentSessions.Remove(user);
                }
            }
        }

        public List<SensorData> GetUserUpdates(User user)
        {
            List<SensorData> result = new List<SensorData>();

            var queue = GetUserQueue(user);
            if (queue != null)
            {
                result.AddRange(queue.GetSensorsUpdates());
            }

            return result;
        }

        public void AddSensorData(SensorData message)
        {
            lock (_accessLock)
            {
                foreach (var pair in _currentSessions)
                {
                    //Use for test environment only, uncomment later
                    if (pair.Key.IsSensorAvailable(message.Key))
                    {
                        pair.Value.AddUpdate(message);
                    }
                    //pair.Value.AddUpdate(message);
                }
            }
        }

        public void AddHistoryItem(string productName, string path)
        {
            throw new NotImplementedException();
        }

        #endregion
        
        private ClientMonitoringQueue GetUserQueue(User user)
        {
            lock (_accessLock)
            {
                var corresponding =  _currentSessions.FirstOrDefault(p => p.Key.IsSame(user));
                return corresponding.Value ?? null;
            }
        }
    }
}
