using HSMServer.Core.Model.Authentication;
using HSMServer.Core.Model.Sensor;
using System.Collections.Generic;
using System.Net;

namespace HSMServer.Core.MonitoringServerCore
{
    internal interface IMonitoringQueueManager
    {
        #region Common queue

        public bool IsUserRegistered(User user);
        public void AddUserSession(User user);
        public void AddUserSession(User user, IPAddress address, int port);
        public void RemoveUserSession(User user);
        public List<SensorData> GetUserUpdates(User user);
        public void AddSensorData(SensorData message);

        #endregion

        #region History queue

        //public void AddHistoryItem(string productName, string path);
        //public bool ISensorHistoryStarted(User user, string productName, string path);
        //public void StartSensorHistory(User user, string productName, string path);
        //public List<SensorHistoryMessage> GetHistoryUpdates(User user, string productName, string path, int n);

        #endregion

    }
}
