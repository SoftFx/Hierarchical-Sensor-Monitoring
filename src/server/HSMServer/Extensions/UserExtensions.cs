﻿using HSMServer.Core.Model;
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
                filter.HasTelegramNotifications == user.Notifications.IsSensorEnabled(sensor.Id) ||
                filter.IsIgnoredSensors == user.Notifications.IsSensorIgnored(sensor.Id);


            if ((filterMask & sensor.GetStateMask(user)) == filterMask)
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

        private static FilterGroups GetStateMask(this SensorNodeViewModel sensor, User user)
        {
            var sensorStateMask = FilterGroups.ByStatus | FilterGroups.ByHistory;
            if (user.Notifications.IsSensorEnabled(sensor.Id) || user.Notifications.IsSensorIgnored(sensor.Id))
                sensorStateMask |= FilterGroups.ByNotifications;
            // TODO: by state

            return sensorStateMask;
        }
    }
}
