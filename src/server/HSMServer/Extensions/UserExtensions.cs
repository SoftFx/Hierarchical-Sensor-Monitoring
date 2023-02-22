using HSMServer.Core.Model;
using HSMServer.Core.Model.UserFilters;
using HSMServer.Model.Authentication;
using HSMServer.Model.TreeViewModel;

namespace HSMServer.Extensions
{
    public static class UserExtensions
    {
        private const FilterGroupType DefaultNodeMask = FilterGroupType.ByStatus | FilterGroupType.ByHistory;


        public static bool IsSensorVisible(this User user, SensorNodeViewModel sensor)
        {
            var filter = user.TreeFilter;
            var filterMask = filter.ToMask();

            if (filterMask == 0)
                return sensor.HasData;

            if ((filterMask & sensor.GetStateMask(user)) == filterMask)
            {
                var filteredSensor = new FilteredSensor()
                {
                    IsNotificationsEnabled = user.Notifications.IsSensorEnabled(sensor.Id) || sensor.RootProduct.Notifications.IsSensorEnabled(sensor.Id),
                    IsNotificationsIgnored = user.Notifications.IsSensorIgnored(sensor.Id) || sensor.RootProduct.Notifications.IsSensorIgnored(sensor.Id),
                    HasData = sensor.HasData,
                    Status = sensor.Status.ToCore(),
                    State = sensor.State,
                };

                return filter.IsSensorVisible(filteredSensor);
            }

            return false;
        }

        public static bool IsEmptyProductVisible(this User user, ProductNodeViewModel product)
        {
            if (!product.IsEmpty)
                return false;

            var filter = user.TreeFilter;
            var filterMask = filter.ToMask();

            if (filterMask != 0 && (filterMask & DefaultNodeMask) == filterMask)
            {
                bool isProductVisible = filterMask.HasFlag(FilterGroupType.ByHistory);

                if (filterMask.HasFlag(FilterGroupType.ByStatus))
                    isProductVisible &= filter.ByStatus.IsStatusSuitable(product.Status.ToCore());

                return isProductVisible;
            }

            return false;
        }

        public static User WithoutPassword(this User user) =>
            new()
            {
                UserName = user.UserName,
                Password = null,
                IsAdmin = user.IsAdmin,
                ProductsRoles = user.ProductsRoles,
                Notifications = new(user.Notifications.ToEntity()),
                TreeFilter = user.TreeFilter
            };

        private static FilterGroupType GetStateMask(this SensorNodeViewModel sensor, User user)
        {
            var sensorStateMask = DefaultNodeMask;

            if (user.Notifications.IsSensorEnabled(sensor.Id) || user.Notifications.IsSensorIgnored(sensor.Id) ||
                sensor.GroupNotifications.IsSensorEnabled(sensor.Id) || sensor.GroupNotifications.IsSensorIgnored(sensor.Id))
                sensorStateMask |= FilterGroupType.ByNotifications;
            if (sensor.State == SensorState.Ignored)
                sensorStateMask |= FilterGroupType.ByState;

            return sensorStateMask;
        }
    }
}
