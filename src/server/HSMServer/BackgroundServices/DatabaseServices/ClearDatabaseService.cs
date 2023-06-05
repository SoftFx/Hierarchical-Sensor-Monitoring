using HSMServer.Core.Cache;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.BackgroundServices
{
    public class ClearDatabaseService : BaseDelayedBackgroundService
    {
        private readonly ITreeValuesCache _cache;


        public override TimeSpan Delay { get; } = TimeSpan.FromHours(1);


        public ClearDatabaseService(ITreeValuesCache cache)
        {
            _cache = cache;
        }


        protected override Task ExecuteAsync(CancellationToken token)
        {
            ServiceAction();

            return base.ExecuteAsync(token);
        }

        protected override Task ServiceAction()
        {
            //RunAction(RunSelfDestroy);
            RunAction(RunClearHistory);

            return Task.CompletedTask;
        }


        private void RunSelfDestroy()
        {
            var sensors = _cache.GetSensors().Where(s => s.ShouldDestroy).ToList();

            foreach (var sensor in sensors)
            {
                var id = sensor.Id;

                _logger.Info("Start removing: {id} product {product} path {path}", id, sensor.RootProductName, sensor.Path);

                _cache.RemoveSensor(id);

                _logger.Info("Stop removing: {id} product {product} path {path}", id, sensor.RootProductName, sensor.Path);
            }
        }

        private void RunClearHistory()
        {
            foreach (var sensor in _cache.GetSensors())
            {
                var id = sensor.Id;

                _logger.Info("Start clear: {id} product {product} path {path}", id, sensor.RootProductName, sensor.Path);

                _cache.CheckSensorHistory(id);

                _logger.Info("Stop clear: {id} product {product} path {path}", id, sensor.RootProductName, sensor.Path);
            }
        }
    }
}
