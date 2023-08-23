using HSMServer.BackgroundServices;

namespace HSMPingModule.Collector
{
    internal sealed class DatacollectorService : BaseDelayedBackgroundService
    {
        private readonly TimeSpan _initDelay = TimeSpan.FromSeconds(10);
        private readonly DataCollectorWrapper _collector;


        public override TimeSpan Delay { get; } = TimeSpan.FromSeconds(5);


        internal DatacollectorService(DataCollectorWrapper collector)
        {
            _collector = collector;
        }


        public override Task StopAsync(CancellationToken _) => _collector.Stop();


        protected override async Task ExecuteAsync(CancellationToken token)
        {
            await Task.Delay(_initDelay, token);

            await _collector.Start();
        }

        protected override Task ServiceAction() => Task.CompletedTask;
    }
}
