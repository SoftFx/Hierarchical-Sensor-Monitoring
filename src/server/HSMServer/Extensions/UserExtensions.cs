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


            bool SensorHasVisibleNotificationsState() =>
                filter.HasTelegramNotifications == user.Notifications.IsSensorEnabled(sensor.Id) ||
                filter.IsIgnoredSensors == user.Notifications.IsSensorIgnored(sensor.Id);


            if ((filterMask & sensor.GetStateMask(user)) == filterMask)
            {
                bool isSensorVisible = true;

                if (filterMask.HasFlag(FilterGroups.ByStatus))
                    isSensorVisible &= sensor.HasVisibleStatus(filter);
                if (!filterMask.HasFlag(FilterGroups.ByHistory))
                    isSensorVisible &= sensor.HasData;
                if (filterMask.HasFlag(FilterGroups.ByNotifications))
                    isSensorVisible &= SensorHasVisibleNotificationsState();
                if (filterMask.HasFlag(FilterGroups.ByState))
                    isSensorVisible &= sensor.State == SensorState.Blocked;

                return isSensorVisible;
            }

            return false;
        }

        public static bool IsEmptyProductVisible(this User user, ProductNodeViewModel product)
        {
            const FilterGroups productStateMask = FilterGroups.ByStatus | FilterGroups.ByHistory;

            var filter = user.TreeFilter;
            var filterMask = filter.ToMask();

            if (filterMask != 0 && (filterMask & productStateMask) == filterMask)
            {
                bool isProductVisible = filterMask.HasFlag(FilterGroups.ByHistory);

                if (filterMask.HasFlag(FilterGroups.ByStatus))
                    isProductVisible &= product.HasVisibleStatus(filter);

                return isProductVisible;
            }

            return false;
        }

        private static FilterGroups GetStateMask(this SensorNodeViewModel sensor, User user)
        {
            var sensorStateMask = FilterGroups.ByStatus | FilterGroups.ByHistory;
            if (user.Notifications.IsSensorEnabled(sensor.Id) || user.Notifications.IsSensorIgnored(sensor.Id))
                sensorStateMask |= FilterGroups.ByNotifications;
            if (sensor.State == SensorState.Blocked)
                sensorStateMask |= FilterGroups.ByState;

            return sensorStateMask;
        }

        private static bool HasVisibleStatus(this NodeViewModel node, TreeUserFilter filter) =>
            node.Status switch
            {
                SensorStatus.Ok => filter.HasOkStatus,
                SensorStatus.Warning => filter.HasWarningStatus,
                SensorStatus.Error => filter.HasErrorStatus,
                SensorStatus.Unknown => filter.HasUnknownStatus,
                _ => false
            };
    }
}
