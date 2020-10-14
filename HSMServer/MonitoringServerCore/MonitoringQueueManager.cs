using System;
using System.Collections.Generic;
using System.Linq;
using HSMServer.Authentication;
using HSMServer.Extensions;
using NLog;
using SensorsService;

namespace HSMServer.MonitoringServerCore
{
    public class MonitoringQueueManager : IMonitoringQueueManager, IDisposable
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
                    _currentSessions[user] = queue;
                }
            }
        }

        private void QueueOverflow(object sender, ClientMonitoringQueue e)
        {
            lock (_accessLock)
            {
                var correspondingUser = _currentSessions.Keys.FirstOrDefault(u => u.UserName.Equals(e.GetUserName()));
                if (correspondingUser != null)
                {
                    _currentSessions[correspondingUser].Clear();
                    _currentSessions.Remove(correspondingUser);
                }
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

        public List<SensorUpdateMessage> GetUserUpdates(User user)
        {
            List<SensorUpdateMessage> result = new List<SensorUpdateMessage>();
            lock (_accessLock)
            {
                result.AddRange(_currentSessions[user].GetSensorUpdateMessages());
            }

            return result;
        }

        public void AddSensorData(SensorUpdateMessage message)
        {
            lock (_accessLock)
            {
                foreach (var pair in _currentSessions)
                {
                    if (pair.Key.IsSensorAvailable(message.Server, message.Name))
                    {
                        pair.Value.AddUpdate(message);
                    }
                }
            }
        }
    }
}
