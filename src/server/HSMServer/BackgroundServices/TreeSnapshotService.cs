using HSMServer.Core.TreeStateSnapshot;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.BackgroundServices
{
    public class TreeSnapshotService : BackgroundService
    {
        private readonly IHostApplicationLifetime _lifetimeHost;
        private readonly ITreeStateSnapshot _snapshot;


        public TreeSnapshotService(IHostApplicationLifetime lifetimeHost, ITreeStateSnapshot snapshot)
        {
            _lifetimeHost = lifetimeHost;
            _snapshot = snapshot;
        }


        public override Task StartAsync(CancellationToken token)
        {
            _lifetimeHost.ApplicationStarted.Register(OnStarted);
            _lifetimeHost.ApplicationStopping.Register(OnStopping);

            return base.StartAsync(token);
        }

        public override Task StopAsync(CancellationToken token) => base.StopAsync(token);

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            return Task.CompletedTask;
        }

        private void OnStarted()
        {
            Console.WriteLine("SNAPSHOT START!");
        }

        private void OnStopping()
        {
            _snapshot.FlushState();
            Console.WriteLine("SNAPSHOT STOPPING!");
        }
    }
}