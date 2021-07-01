using System;
using System.Threading.Tasks;
using HSMCommon.Model.SensorsData;
using HSMServer.Authentication;
using HSMServer.Model.ViewModel;
using Microsoft.AspNetCore.SignalR;

namespace HSMServer.SignalR
{
    public class MonitoringDataHub : Hub<IMonitoringDataHub>
    {
        //private readonly TimeSpan _updateInterval = TimeSpan.FromSeconds(3);
        //private readonly Timer _timer;
        //private readonly IMonitoringCore _monitoringCore;
        private readonly ISignalRSessionsManager _sessionsManager;
        public MonitoringDataHub(ISignalRSessionsManager sessionsManager)
        {
            //_monitoringCore = monitoringCore;
            //_timer = new Timer(GetUpdate, null, _updateInterval, _updateInterval);
            _sessionsManager = sessionsManager;
            //_sessionsManager.AddConnection(Context.User as User, Context.ConnectionId);
        }

        public override Task OnConnectedAsync()
        {
            if (Context != null)
            {
                _sessionsManager.AddConnection(Context.User as User, Context.ConnectionId);
            }
            return base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception? exception)
        {
            if (Context != null)
            {
                _sessionsManager.RemoveConnection(Context.User as User);
            }
            return base.OnDisconnectedAsync(exception);
        }
        //public async Task Send(List<SensorData> sensors)
        //{
        //    TreeViewModel tree = new TreeViewModel(sensors);

        //    await Clients.Caller.SendAsync("Receive", tree);
        //}

        //private void GetUpdate(object state)
        //{
        //    var usr = Context.User as User;
        //    _monitoringCore.GetSensorUpdates(Context.User as User);
        //}
    }
}
