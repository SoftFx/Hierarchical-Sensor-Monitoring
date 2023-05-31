using HSMServer.Core.Cache;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.BackgroundTask
{
    public class ClearDatabaseService : BackgroundService
    {
        private readonly ILogger<ClearDatabaseService> _logger;
        private readonly ITreeValuesCache _cache;

        private readonly TimeSpan _delay = new(6, 0, 0);


        public ClearDatabaseService(ITreeValuesCache cache, ILogger<ClearDatabaseService> logger)
        {
            _logger = logger;
            _cache = cache;
        }


        protected override async Task ExecuteAsync(CancellationToken token)
        {
            ClearData();

            var delay = _delay.Ticks;
            var start = new DateTime(DateTime.UtcNow.Ticks / delay * delay + delay);

            await Task.Delay(start - DateTime.UtcNow, token);

            while (!token.IsCancellationRequested)
            {
                ClearData();

                await Task.Delay(_delay, token);
            }
        }

        private void ClearData()
        {
            RunClearHistory();
            //RunSelfDestroy();
        }

        private void RunSelfDestroy()
        {
            _logger.LogInformation($"Start {nameof(RunSelfDestroy)}");

            var sensors = _cache.GetSensors().Where(s => s.ShouldDestroy).ToList();

            foreach (var sensor in sensors)
            {
                var id = sensor.Id;

                _logger.LogInformation("Start removing: {id} product {product} path {path}", id, sensor.RootProductName, sensor.Path);

                _cache.RemoveSensor(id);

                _logger.LogInformation("Stop removing: {id} product {product} path {path}", id, sensor.RootProductName, sensor.Path);
            }

            _logger.LogInformation($"Stop {nameof(RunSelfDestroy)}");
        }

        private void RunClearHistory()
        {
            _logger.LogInformation($"Start {nameof(RunClearHistory)}");

            var utcNow = DateTime.UtcNow;

            foreach (var sensor in _cache.GetSensors())
            {
                var id = sensor.Id;

                _logger.LogInformation("Start clear: {id} product {product} path {path}", id, sensor.RootProductName, sensor.Path);

                _cache.CheckSensorHistory(id);

                _logger.LogInformation("Stop clear: {id} product {product} path {path}", id, sensor.RootProductName, sensor.Path);
            }

            _logger.LogInformation($"Stop {nameof(RunClearHistory)}");
        }
    }
}
