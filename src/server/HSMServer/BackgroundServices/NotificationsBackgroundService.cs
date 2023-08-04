using HSMServer.Notifications;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.BackgroundServices
{
    public class NotificationsBackgroundService : BaseDelayedBackgroundService
    {
        private readonly NotificationsCenter _center;


        public override TimeSpan Delay { get; } = TimeSpan.FromSeconds(10);


        public NotificationsBackgroundService(NotificationsCenter center) 
        {
            _center = center;
        }


        public override Task StartAsync(CancellationToken cancellationToken) => _center.Start();

        public override Task StopAsync(CancellationToken token) => _center.DisposeAsync().AsTask();


        protected override Task ServiceAction()
        {
            _center.CheckState();

            return Task.CompletedTask;
        }
    }
}
