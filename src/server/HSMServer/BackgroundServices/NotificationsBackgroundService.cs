using HSMServer.Notifications;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.BackgroundServices
{
    public class NotificationsBackgroundService : BaseDelayedBackgroundService
    {
        private const int CenterRecalculationPeriodSec = 30;

        private readonly NotificationsCenter _center;
        private DateTime _lastCenterRecalculation = DateTime.MinValue;


        public override TimeSpan Delay { get; } = TimeSpan.FromSeconds(1);


        public NotificationsBackgroundService(NotificationsCenter center)
        {
            _center = center;
        }


        public override Task StartAsync(CancellationToken cancellationToken) => _center.Start();

        public override Task StopAsync(CancellationToken token) => _center.DisposeAsync().AsTask();


        protected override Task ServiceAction()
        {
            _center.SendAllMessages();

            if ((DateTime.UtcNow - _lastCenterRecalculation).TotalSeconds >= CenterRecalculationPeriodSec)
            {
                _lastCenterRecalculation = DateTime.UtcNow;
                _center.RecalculateState();
            }

            return Task.CompletedTask;
        }
    }
}
