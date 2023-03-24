using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Notifications;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.BackgroundTask
{
    public sealed class MonitoringBackgroundService : BackgroundService
    {
        private const int Delay = 60000; // 1 minute

        private readonly ITreeValuesCache _cache;
        private readonly IUserManager _userManager;
        private readonly TelegramBot _telegramBot;


        public MonitoringBackgroundService(ITreeValuesCache cache, IUserManager userManager, INotificationsCenter notifications)
        {
            _cache = cache;
            _userManager = userManager;
            _telegramBot = notifications.TelegramBot;
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_cache.IsInitialized)
                {
                    ValidateSensors();
                    UpdateAccessKeysState();
                    RemoveOutdatedIgnoredNotifications();
                    RemoveExpiredInvitationTokens();
                    UpdateMutedSensorsState();
                }

                await Task.Delay(Delay, stoppingToken);
            }
        }

        private void ValidateSensors()
        {
            foreach (var sensor in _cache.GetSensors())
            {
                var oldStatus = sensor.ValidationResult;

                if (sensor.CheckExpectedUpdateInterval())
                    _cache.NotifyAboutChanges(sensor, oldStatus);
            }
        }

        private void UpdateAccessKeysState()
        {
            foreach (var key in _cache.GetAccessKeys())
                _cache.CheckAccessKeyExpiration(key);
        }

        private void RemoveOutdatedIgnoredNotifications()
        {
            foreach (var user in _userManager.GetUsers())
            {
                bool needUpdateUser = false;

                foreach (var (sensorId, endOfIgnorePeriod) in user.Notifications.IgnoredSensors)
                    if (DateTime.UtcNow >= endOfIgnorePeriod)
                    {
                        user.Notifications.RemoveIgnore(sensorId);
                        needUpdateUser = true;
                    }

                if (needUpdateUser)
                    _userManager.UpdateUser(user);
            }

            foreach (var product in _cache.GetProducts())
            {
                bool needUpdate = false;

                foreach (var (sensorId, endOfIgnorePeriod) in product.Notifications.IgnoredSensors)
                    if (DateTime.UtcNow >= endOfIgnorePeriod)
                    {
                        product.Notifications.RemoveIgnore(sensorId);
                        needUpdate = true;
                    }

                if (needUpdate)
                    _cache.UpdateProduct(product);
            }
        }

        private void UpdateMutedSensorsState()
        {
            foreach (var sensor in _cache.GetSensors())
                if (sensor.EndOfMuting <= DateTime.UtcNow)
                    _cache.UpdateMutedSensorState(sensor.Id);
        }

        private void RemoveExpiredInvitationTokens() => _telegramBot.RemoveOldInvitationTokens();
    }
}
