using System;
using System.Threading;
using HSMCommon;
using HSMServer.Authentication;
using HSMServer.MonitoringServerCore;
using HSMServer.SignalR;
using Microsoft.AspNetCore.SignalR;

namespace HSMServer.Services
{
    public class ClientMonitoringService : IClientMonitoringService
    {
        private readonly TimeSpan _updateInterval = TimeSpan.FromSeconds(3);
        private Timer _timer;
        private readonly IHubContext<MonitoringDataHub> _monitoringDataHubContext;
        private readonly IMonitoringCore _monitoringCore;
        private readonly User _user;
        private readonly ISignalRSessionsManager _sessionsManager;
        public ClientMonitoringService(IHubContext<MonitoringDataHub> hubContext, IMonitoringCore monitoringCore,
            UserManager userManager, ISignalRSessionsManager sessionsManager)
        {
            _monitoringDataHubContext = hubContext;
            _monitoringCore = monitoringCore;
            //TODO: REMOVE WHEN MAKE NORMANL AUTH
            _user = userManager.GetUserByCertificateThumbprint(CommonConstants.DefaultClientCertificateThumbprint);
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
