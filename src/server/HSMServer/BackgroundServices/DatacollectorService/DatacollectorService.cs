using System;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.BackgroundServices
{
    public class DatacollectorService : BaseDelayedBackgroundService
    {
        private readonly TimeSpan _initDelay = TimeSpan.FromSeconds(10);
        private readonly DataCollectorWrapper _collector;


        public override TimeSpan Delay { get; } = TimeSpan.FromMinutes(5);


        public DatacollectorService(DataCollectorWrapper collector)
        {
            _collector = collector;
        }


        public override Task StopAsync(CancellationToken _) => _collector.Stop();


        protected override async Task ExecuteAsync(CancellationToken token)
        {
            await Task.Delay(_initDelay, token); //small delay wait server initializing
            await _collector.Start();

            await base.ExecuteAsync(token);
        }

        protected override Task ServiceAction()
        {
            _collector.SendDbInfo();

            return Task.CompletedTask;
        }
    }
}
