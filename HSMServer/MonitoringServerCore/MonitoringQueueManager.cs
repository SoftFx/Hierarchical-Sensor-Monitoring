using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMServer.Authentication;
using HSMServer.Extensions;
using NLog;
using SensorsService;

namespace HSMServer.MonitoringServerCore
{
    public class MonitoringQueueManager : IMonitoringQueueManager
    {
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
                if (_currentSessions.ContainsKey(user))
                {
                    _currentSessions[user].Clear();
                    _currentSessions.Remove(user);
                }

                _currentSessions[user] = new ClientMonitoringQueue();
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
