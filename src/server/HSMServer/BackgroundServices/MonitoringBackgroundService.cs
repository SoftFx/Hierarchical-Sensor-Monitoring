using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Model.TreeViewModel;
using HSMServer.Notification.Settings;
using HSMServer.Notifications;
using System;
using System.Threading.Tasks;

namespace HSMServer.BackgroundServices
{
    public sealed class MonitoringBackgroundService : BaseDelayedBackgroundService
    {
        private readonly NotificationsCenter _notifications;
        private readonly IUserManager _userManager;
        private readonly ITreeValuesCache _cache;
        private readonly TreeViewModel _tree;

        public override TimeSpan Delay { get; } = new TimeSpan(0, 1, 1); // 1 extra second to apply all updates


        public MonitoringBackgroundService(ITreeValuesCache cache, TreeViewModel tree, IUserManager userManager, NotificationsCenter notifications)
        {
            _notifications = notifications;
            _userManager = userManager;
            _cache = cache;
            _tree = tree;
        }


        protected override Task ServiceAction()
        {
            if (_cache.IsInitialized)
            {
                _cache.UpdateCacheState();
                _notifications.CheckNotificationCenterState();

                RemoveOutdatedIgnoredNotifications();
            }

            return Task.CompletedTask;
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
