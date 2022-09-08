using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using HSMServer.Model.TreeViewModels;

namespace HSMServer.Extensions
{
    public static class UserExtensions
    {
        public static bool IsSensorVisible(this User user, SensorNodeViewModel sensor)
        {
            var filter = user.TreeFilter;
            var filterMask = filter.ToMask();

            if (filterMask == 0)
                return sensor.HasData;


            bool SensorHasVisibleStatus() =>
                sensor.Status switch
                {
                    SensorStatus.Ok => filter.HasOkStatus,
                    SensorStatus.Warning => filter.HasWarningStatus,
                    SensorStatus.Error => filter.HasErrorStatus,
                    SensorStatus.Unknown => filter.HasUnknownStatus,
                    _ => false
                };

            bool SensorHasVisibleNotificationsState() =>
                filter.HasTelegramNotifications == user.Notifications.EnabledSensors.Contains(sensor.Id) ||
                filter.IsIgnoredSensors == user.Notifications.IgnoredSensors.ContainsKey(sensor.Id);

            FilterGroups GetSensorStateMask()
            {
                var sensorStateMask = FilterGroups.ByStatus | FilterGroups.ByHistory;
                if (user.Notifications.EnabledSensors.Contains(sensor.Id) || user.Notifications.IgnoredSensors.ContainsKey(sensor.Id))
                    sensorStateMask |= FilterGroups.ByNotifications;
                // TODO: by state

                return sensorStateMask;
            }


            if ((filterMask & GetSensorStateMask()) == filterMask)
            {
                bool isSensorVisible = true;

                if (filterMask.HasFlag(FilterGroups.ByStatus))
                    isSensorVisible &= SensorHasVisibleStatus();
                if (!filterMask.HasFlag(FilterGroups.ByHistory))
                    isSensorVisible &= sensor.HasData;
                if (filterMask.HasFlag(FilterGroups.ByNotifications))
                    isSensorVisible &= SensorHasVisibleNotificationsState();
                // TODO: by state

                return isSensorVisible;
            }

            return false;
        }
    }
}
