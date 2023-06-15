using HSMServer.Core.Cache;
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
        private readonly ITreeValuesCache _cache;


        public override TimeSpan Delay { get; } = TimeSpan.FromMinutes(5);


        public TreeSnapshotService(IHostApplicationLifetime lifetimeHost, ITreeStateSnapshot snapshot,
                                   ITreeValuesCache cache)
        {
            _lifetimeHost = lifetimeHost;
            _snapshot = snapshot;
            _cache = cache;
        }


        public override Task StartAsync(CancellationToken token)
        {
            _lifetimeHost.ApplicationStopping.Register(async () => await StopHandler());

            return base.StartAsync(token);
        }

        protected override Task ServiceAction() => SaveState(false);


        private async Task SaveState(bool isFinal)
        {
            _logger.Info($"Start state flushing");

            await _snapshot.FlushState(isFinal);

            _logger.Info($"Stop state flushing");
        }

        private Task StopHandler()
        {
            _cache.SaveLastStateToDb();

            return SaveState(true);
        }
    }
}