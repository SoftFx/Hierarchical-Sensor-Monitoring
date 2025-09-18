using HSMServer.Core.Cache;
using HSMServer.Core.TableOfChanges;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.BackgroundServices
{
    public class ClearDatabaseService : BaseDelayedBackgroundService
    {
        private readonly ITreeValuesCache _cache;


        public override TimeSpan Delay { get; } = TimeSpan.FromMinutes(1);


        public ClearDatabaseService(ITreeValuesCache cache)
        {
            _cache = cache;
        }

        //uncomment for immediately running
        //protected override Task ExecuteAsync(CancellationToken token)
        //{
        //    ServiceAction();
        //    return base.ExecuteAsync(token);
        //}

        protected override async Task ServiceActionAsync()
        {
            await RunActionAsync(RunClearHistory);
            await RunActionAsync(RunSensorsSelfDestroy);
            await RunActionAsync(RunProductsSelfDestroy);
        }


        private async Task RunClearHistory()
        {
            foreach (var sensor in _cache.GetSensors())
            {
                var id = sensor.Id;

                _logger.Trace("Start clear: {id} {product}{path}", id, sensor.RootProductName, sensor.Path);

                await _cache.CheckSensorHistoryAsync(id);

                _logger.Trace("Stop clear: {id} {product}{path}", id, sensor.RootProductName, sensor.Path);
            }
        }

        private async Task RunSensorsSelfDestroy()
        {
            foreach (var sensor in _cache.GetSensors())
            {
                var id = sensor.Id;

                if (sensor.ShouldDestroy())
                {

                    _logger.Trace("Start removing: {id} {product}{path}", id, sensor.RootProductName, sensor.Path);

                    await _cache.RemoveSensorAsync(id, InitiatorInfo.AsSystemInfo("Clean up"));

                    _logger.Trace("Stop removing: {id} {product}{path}", id, sensor.RootProductName, sensor.Path);
                }
            }
        }

        private async Task RunProductsSelfDestroy()
        {
            foreach (var product in _cache.GetProducts())
            {
                var id = product.Id;

                _logger.Trace("Start clear scanner: {id} {product}", id, product.DisplayName);

                await _cache.ClearEmptyNodesAsync(product);

                _logger.Trace("Stop clear scanner: {id} {product}", id, product.DisplayName);
            }
        }
    }
}
