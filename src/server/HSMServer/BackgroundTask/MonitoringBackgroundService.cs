using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Model.TreeViewModel;
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

        private readonly NotificationsCenter _notifications;
        private readonly IUserManager _userManager;
        private readonly ITreeValuesCache _cache;
        private readonly TreeViewModel _tree;


        public MonitoringBackgroundService(ITreeValuesCache cache, TreeViewModel tree, IUserManager userManager, NotificationsCenter notifications)
        {
            _notifications = notifications;
            _userManager = userManager;
            _cache = cache;
            _tree = tree;
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_cache.IsInitialized)
                {
                    _cache.UpdateCacheState();
                    _notifications.CheckNotificationCenterState();

                    RemoveOutdatedIgnoredNotifications();
                }

                await Task.Delay(Delay, stoppingToken);
            }
        }


        private void RemoveOutdatedIgnoredNotifications()
        {
            foreach (var user in _userManager.GetUsers())
                if (CheckResaveNotificationsState(user))
                    _userManager.UpdateUser(user);

            foreach (var product in _tree.GetRootProducts())
                if (CheckResaveNotificationsState(product))
                    _cache.UpdateProduct(_cache.GetProduct(product.Id));
        }

        private static bool CheckResaveNotificationsState(INotificatable entity)
        {
            bool needResave = false;

            foreach (var (sensorId, endOfIgnorePeriod) in entity.Notifications.IgnoredSensors)
                if (DateTime.UtcNow >= endOfIgnorePeriod)
                {
                    entity.Notifications.RemoveIgnore(sensorId);
                    needResave = true;
                }

            return needResave;
        }
    }
}
