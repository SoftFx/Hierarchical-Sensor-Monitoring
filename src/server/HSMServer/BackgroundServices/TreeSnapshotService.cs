using HSMServer.BackgroundServices.DatabaseServices;
using HSMServer.Core.TreeStateSnapshot;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.BackgroundServices
{
    public class TreeSnapshotService : BaseDelayedBackgroundService
    {
        private readonly IHostApplicationLifetime _lifetimeHost;
        private readonly ITreeStateSnapshot _snapshot;


        public override TimeSpan Delay { get; } = new TimeSpan(0, 5, 0);


        public TreeSnapshotService(IHostApplicationLifetime lifetimeHost, ITreeStateSnapshot snapshot)
        {
            _lifetimeHost = lifetimeHost;
            _snapshot = snapshot;
        }


        public override Task StartAsync(CancellationToken token)
        {
            _lifetimeHost.ApplicationStarted.Register(OnStarted);
            _lifetimeHost.ApplicationStopping.Register(ServiceAction);

            return base.StartAsync(token);
        }

        protected override void ServiceAction()
        {
            _logger.Info($"Start state flushing");

            _snapshot.FlushState();

            _logger.Info($"Stop state flushing");

            Console.Write("Flush state");
        }

        private void OnStarted()
        {
            Console.WriteLine("SNAPSHOT START!");
        }
    }
}