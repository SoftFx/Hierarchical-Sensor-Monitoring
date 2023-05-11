using HSM.Core.Monitoring;
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


        public DatacollectorService(DataCollectorWrapper collector, ILogger<DatacollectorService> logger)
        {
            _collector = collector;

            _logger = logger;
            _logger.LogInformation($"{nameof(DatacollectorService)} initialized!");
        }


        public override Task StartAsync(CancellationToken _)
        {
            var task = _collector.Start();

            _logger.LogInformation($"{nameof(DatacollectorService)} started!");

            return task;
        }

        public override Task StopAsync(CancellationToken _)
        {
            var task = _collector.Stop();

            _logger.LogInformation($"{nameof(DatacollectorService)} stopped!");

            return task;
        }


        protected override async Task ExecuteAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                _collector.SendDbInfo();

                await Task.Delay(_sleepPeriod, token);
            }
        }
    }
}
