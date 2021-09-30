using HSMCommon.Constants;
using HSMServer.Core.Authentication;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.MonitoringServerCore;
using HSMServer.SignalR;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Threading;

namespace HSMServer.Services
{
    public class ClientMonitoringService : IClientMonitoringService
    {
        private readonly TimeSpan _updateInterval = TimeSpan.FromSeconds(3);
        private Timer _timer;
        private readonly IHubContext<MonitoringDataHub> _monitoringDataHubContext;
        private readonly IMonitoringCore _monitoringCore;
        private readonly ISignalRSessionsManager _sessionsManager;
        public ClientMonitoringService(IHubContext<MonitoringDataHub> hubContext, IMonitoringCore monitoringCore,
            IUserManager userManager, ISignalRSessionsManager sessionsManager)
        {
            _monitoringDataHubContext = hubContext;
            _monitoringCore = monitoringCore;
            //TODO: REMOVE WHEN MAKE NORMANL AUTH
            _sessionsManager = sessionsManager;
            //StartTimer();
        }
        public void Initialize()
        {
            StartTimer();
        }
        private void StartTimer()
        {
            _timer = new Timer(Timer_Tick, null, _updateInterval, _updateInterval);
        }

        private void Timer_Tick(object state)
        {
            var dictionary = _sessionsManager.UserConnectionDictionary;
            foreach (var pair in dictionary)
            {
                var updates = _monitoringCore.GetSensorUpdates(pair.Key);

                if (updates.Count < 1)
                    continue;

                _monitoringDataHubContext.Clients.Client(pair.Value)
                    .SendAsync(nameof(IMonitoringDataHub.SendSensorUpdates), updates);
            }
        }
    }
}
