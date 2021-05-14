using HSMCommon.Model.SensorsData;
using HSMServer.Authentication;
using HSMServer.Model.ViewModel;
using HSMServer.MonitoringServerCore;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.SignalR
{
    public class MonitoringHub : Hub
    {
        private readonly TimeSpan _updateInterval = TimeSpan.FromSeconds(3);
        private readonly Timer _timer;
        private readonly IMonitoringCore _monitoringCore;

        public MonitoringHub(IMonitoringCore monitoringCore)
        {
            _monitoringCore = monitoringCore;
           // _timer = new Timer(GetUpdate, null, _updateInterval, _updateInterval);
        }

        public async Task Send(List<SensorData> sensors)
        {
            TreeViewModel tree = new TreeViewModel(sensors);

            await Clients.Caller.SendAsync("Receive", tree);
        }

        //private void GetUpdate(object state)
        //{
        //     _monitoringCore.GetSensorUpdates(Context.User as User);
        //}
    }
}
