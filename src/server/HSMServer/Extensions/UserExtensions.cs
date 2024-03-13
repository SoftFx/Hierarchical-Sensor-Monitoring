using HSMServer.Core.Model;
using HSMServer.Model.Authentication;
using HSMServer.Model.TreeViewModel;
using HSMServer.UserFilters;
using System.Linq;

namespace HSMServer.Extensions
{
    public static class UserExtensions
    {
        private const FilterGroupType DefaultNodeMask = FilterGroupType.ByStatus | FilterGroupType.ByVisibility;


        public static bool IsSensorVisible(this User user, SensorNodeViewModel sensor)
        {
            var filter = user.TreeFilter;
            var filterMask = filter.ToMask();

            if (filterMask == 0)
                return sensor.HasData;

            if ((filterMask & sensor.GetStateMask()) == filterMask)
            {
                var filteredSensor = new FilteredSensor()
                {
                    HasUnconfiguredAlerts = sensor.DataAlerts.Values.Any(d => d.Any(a => a.IsUnconfigured())) || sensor.TTLAlert.IsUnconfigured(),
                    IsGrafanaEnabled = sensor.Integration.HasFlag(Integration.Grafana),
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
                var isProductVisible = true;

                if (filterMask.HasFlag(FilterGroupType.ByVisibility))
                    isProductVisible &= filter.ByVisibility.Empty.Value;

                if (filterMask.HasFlag(FilterGroupType.ByStatus))
                    isProductVisible &= filter.ByStatus.IsStatusSuitable(product.Status.ToCore());

                return isProductVisible;
            }

            return false;
        }

        private static FilterGroupType GetStateMask(this SensorNodeViewModel sensor)
        {
            var sensorStateMask = DefaultNodeMask;

            if (sensor.State == SensorState.Muted)
                sensorStateMask |= FilterGroupType.ByState;

            if (sensor.Integration.HasFlag(Integration.Grafana))
                sensorStateMask |= FilterGroupType.Integrations;

            if (sensor.DataAlerts.Values.Any(d => d.Any(a => a.IsUnconfigured())) || sensor.TTLAlert.IsUnconfigured())
                sensorStateMask |= FilterGroupType.Alerts;

            return sensorStateMask;
        }
    }
}
