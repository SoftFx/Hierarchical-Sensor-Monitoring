using HSMServer.Core.Model;

namespace HSMServer.UserFilters
{
    // TODO: This class should be removed after User refactoring
    // this class is a temporary solution for solving the problem of relating UserExtensions and TreeUserFilter 
    public class FilteredSensor
    {
        public bool HasUnconfiguredAlerts { get; init; }

        public bool IsGrafanaEnabled { get; init; }

        public SensorStatus Status { get; init; }

        public SensorState State { get; init; }

        public bool HasData { get; init; }
    }
}
