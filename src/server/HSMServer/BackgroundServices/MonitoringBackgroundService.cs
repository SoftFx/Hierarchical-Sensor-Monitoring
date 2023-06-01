using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Model.TreeViewModel;
using HSMServer.Notification.Settings;
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


        protected override async Task ExecuteAsync(CancellationToken cToken)
        {
            await Task.Delay((60 - DateTime.UtcNow.Second + 1) * 1000, cToken); //task start time alignment

            while (!cToken.IsCancellationRequested)
            {
                if (_cache.IsInitialized)
                {
                    _cache.UpdateCacheState();
                    _notifications.CheckNotificationCenterState();

                    RemoveOutdatedIgnoredNotifications();
                }

                await Task.Delay(Delay, cToken);
            }
        }


        private void RemoveOutdatedIgnoredNotifications()
        {
            foreach (var user in _userManager.GetUsers())
                if (ShouldRemoveIgnoreStatus(user))
                    _userManager.UpdateUser(user);

            foreach (var product in _tree.GetRootProducts())
                if (ShouldRemoveIgnoreStatus(product))
                    _tree.UpdateProductNotificationSettings(product);
        }

        private static bool ShouldRemoveIgnoreStatus(INotificatable entity)
        {
            bool needResave = false;

            foreach (var (chatId, ignoredSensors) in entity.Notifications.PartiallyIgnored)
                foreach (var (sensorId, endOfIgnorePeriod) in ignoredSensors)
                    if (DateTime.UtcNow >= endOfIgnorePeriod)
                    {
                        entity.Notifications.RemoveIgnore(sensorId, chatId);
                        needResave = true;
                    }

            return needResave;
        }
    }
}
