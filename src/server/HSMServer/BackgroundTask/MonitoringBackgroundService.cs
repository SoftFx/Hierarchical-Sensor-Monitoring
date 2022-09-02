using HSMServer.Core.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.Entities;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.BackgroundTask
{
    public sealed class MonitoringBackgroundService : BackgroundService
    {
        private const int Delay = 60000; // 1 minute

        private readonly ITreeValuesCache _treeValuesCache;
        private readonly IUserManager _userManager;


        public MonitoringBackgroundService(ITreeValuesCache treeValuesCache, IUserManager userManager)
        {
            _treeValuesCache = treeValuesCache;
            _userManager = userManager;
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_treeValuesCache.IsInitialized)
                {
                    ValidateSensors();
                    UpdateAccessKeysState();
                    RemoveOutdatedIgnoredSensors();
                }

                await Task.Delay(Delay, stoppingToken);
            }
        }

        private void ValidateSensors()
        {
            foreach (var sensor in _treeValuesCache.GetSensors())
            {
                var oldStatus = sensor.ValidationResult;

                if (sensor.CheckExpectedUpdateInterval())
                    _treeValuesCache.NotifyAboutChanges(sensor, oldStatus);
            }
        }

        private void UpdateAccessKeysState()
        {
            foreach (var key in _treeValuesCache.GetAccessKeys())
                if (key.HasExpired)
                    _treeValuesCache.UpdateAccessKey(new() { Id = key.Id, State = KeyState.Expired });
        }

        private void RemoveOutdatedIgnoredSensors()
        {
            foreach (var user in _userManager.GetUsers())
            {
                bool needUpdateUser = false;

                foreach (var (sensorId, endOfIgnorePeriod) in user.Notifications.IgnoredSensors)
                    if (DateTime.UtcNow >= endOfIgnorePeriod &&
                        user.Notifications.IgnoredSensors.TryRemove(sensorId, out _))
                        needUpdateUser = true;

                if (needUpdateUser)
                    _userManager.UpdateUser(user);
            }
        }
    }
}
