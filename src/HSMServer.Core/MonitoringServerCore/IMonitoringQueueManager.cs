using HSMServer.Core.Model.Authentication;
using HSMServer.Core.Model.Sensor;
using System.Collections.Generic;

namespace HSMServer.Core.MonitoringServerCore
{
    internal interface IMonitoringQueueManager
    {
        #region Common queue

        public bool IsUserRegistered(User user);
        public void AddUserSession(User user);
        public void RemoveUserSession(User user);
        public List<SensorData> GetUserUpdates(User user);
        public void AddSensorData(SensorData message);

        #endregion
    }
}