﻿using HSMServer.Core.Authentication;
using HSMServer.Core.Extensions;
using HSMServer.Core.Helpers;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.Model.Sensor;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.MonitoringServerCore
{
    internal class MonitoringQueueManager : IMonitoringQueueManager
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
                _userManager.UpdateUserEvent -= UpdateUserEventHandler;

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
        private readonly IUserManager _userManager;

        public MonitoringQueueManager(IUserManager userManager)
        {
            _logger = LogManager.GetCurrentClassLogger();
            lock (_accessLock)
            {
                _currentSessions = new Dictionary<User, ClientMonitoringQueue>(new UsersComparer());
            }

            _userManager = userManager;
            _userManager.UpdateUserEvent += UpdateUserEventHandler;
            _logger.Info("Monitoring queue manager initialized");
        }

        #region Interface implementation

        public bool IsUserRegistered(User user)
        {
            bool isRegistered;
            lock (_accessLock)
            {
                isRegistered = _currentSessions.ContainsKey(user);
            }

            return isRegistered;
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
                    if (UserRoleHelper.IsAllSensorsAllowed(pair.Key))
                        pair.Value.AddUpdate(message);

                    else if (pair.Key.IsSensorAvailable(message.Key))
                        pair.Value.AddUpdate(message);
                }
            }
        }

        public void AddSensorDataForUser(User user, SensorData message)
        {
            lock (_accessLock)
            {
                if (_currentSessions.ContainsKey(user))
                {
                    _currentSessions[user].AddUpdate(message);
                }
            }
        }

        #endregion

        private void UpdateUserEventHandler(User user)
        {
            lock (_accessLock)
            {
                var correspondingPair = _currentSessions.FirstOrDefault(p => p.Key.IsSame(user));

                if (correspondingPair.Value != null)
                    correspondingPair.Key?.Update(user);
            }
        }

        private ClientMonitoringQueue GetUserQueue(User user)
        {
            lock (_accessLock)
            {
                bool contains = _currentSessions.ContainsKey(user);
                return contains ? _currentSessions[user] : null;
            }
        }
    }
}
