using HSMServer.Core.Cache;
using HSMServer.Core.Cache.Entities;
using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.BackgroundTask
{
    public sealed class MonitoringBackgroundService : BackgroundService
    {
        private const int Delay = 60000;

        private readonly ITreeValuesCache _treeValuesCache;


        public MonitoringBackgroundService(ITreeValuesCache treeValuesCache)
        {
            _treeValuesCache = treeValuesCache;
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_treeValuesCache.IsInitialized)
                {
                    ValidateSensors();
                    UpdateAccessKeysState();
                }

                await Task.Delay(Delay, stoppingToken);
            }
        }

        private void ValidateSensors()
        {
            foreach (var sensor in _treeValuesCache.GetSensors())
                if (sensor.CheckExpectedUpdateInterval())
                    _treeValuesCache.OnChangeSensorEvent(sensor, TransactionType.Update);
        }

        private void UpdateAccessKeysState()
        {
            foreach (var key in _treeValuesCache.GetAccessKeys())
                if (key.HasExpired())
                    _treeValuesCache.UpdateAccessKey(new() { Id = key.Id, State = KeyState.Expired, });
        }
    }
}
