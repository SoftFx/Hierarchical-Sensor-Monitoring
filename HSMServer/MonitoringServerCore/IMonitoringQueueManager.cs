using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SensorsService;

namespace HSMServer.MonitoringServerCore
{
    public interface IMonitoringQueueManager
    {
        public void AddUserSession();
        public void RemoveUserSession();
        public List<SensorUpdateMessage> GetUserUpdates();
    }
}
