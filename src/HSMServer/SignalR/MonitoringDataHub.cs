using HSMServer.Core.Model.Authentication;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Threading.Tasks;

namespace HSMServer.SignalR
{
    public class MonitoringDataHub : Hub<IMonitoringDataHub>
    {
        private readonly ISignalRSessionsManager _sessionsManager;
        public MonitoringDataHub(ISignalRSessionsManager sessionsManager)
        {
            _sessionsManager = sessionsManager;
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
                _sessionsManager.RemoveConnection(Context.User as User, Context.ConnectionId);
            }
            return base.OnDisconnectedAsync(exception);
        }
    }
}
