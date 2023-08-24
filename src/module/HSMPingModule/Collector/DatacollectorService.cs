namespace HSMPingModule.Collector
{
    internal sealed class DatacollectorService : BackgroundService
    {
        private readonly TimeSpan _initDelay = TimeSpan.FromSeconds(10);
        private readonly DataCollectorWrapper _collector;


        public DatacollectorService(DataCollectorWrapper collector)
        {
            _collector = collector;
        }


        public override Task StopAsync(CancellationToken _) => _collector.Stop();


        protected override async Task ExecuteAsync(CancellationToken token)
        {
            await Task.Delay(_initDelay, token);

            await _collector.Start();
        }
    }
}
