using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.BackgroundTask
{
    public class DatacollectorService : BackgroundService
    {
        private readonly ILogger<DatacollectorService> _logger;
        private readonly DataCollectorWrapper _collector;

        private readonly TimeSpan _sleepPeriod = new(0, 0, 5, 0);
        private readonly TimeSpan _initDelay = new(0, 0, 10);


        public DatacollectorService(DataCollectorWrapper collector, ILogger<DatacollectorService> logger)
        {
            _collector = collector;

            _logger = logger;
            _logger.LogInformation($"{nameof(DatacollectorService)} initialized!");
        }


        public override Task StopAsync(CancellationToken _) => _collector.Stop();


        protected override async Task ExecuteAsync(CancellationToken token)
        {
            await Task.Delay(_initDelay, token); //small delay wait server initializing
            await _collector.Start();

            while (!token.IsCancellationRequested)
            {
                await Task.Delay(_sleepPeriod, token);

                _collector.SendDbInfo();
            }
        }
    }
}
