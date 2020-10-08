using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SensorsService;

namespace HSMServer.MonitoringServerCore
{
    public class MonitoringQueueManager : IMonitoringQueueManager
    {
        private Dictionary<string, ClientMonitoringQueue> _currentSessions;
        public MonitoringQueueManager()
        {
            _currentSessions = new Dictionary<string, ClientMonitoringQueue>();
        }


        public void AddUserSession()
        {
            throw new NotImplementedException();
        }

        public void RemoveUserSession()
        {
            throw new NotImplementedException();
        }

        public List<SensorUpdateMessage> GetUserUpdates()
        {
            throw new NotImplementedException();
        }
    }
}
