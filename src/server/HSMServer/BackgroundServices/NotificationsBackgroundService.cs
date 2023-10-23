using HSMServer.Notifications;
using Newtonsoft.Json.Linq;
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


        public override Task StartAsync(CancellationToken token) => _center.Start().ContinueWith(_ => base.StartAsync(token)).Unwrap();

        public override Task StopAsync(CancellationToken token) => _center.DisposeAsync().AsTask().ContinueWith(_ => base.StopAsync(token)).Unwrap();


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
