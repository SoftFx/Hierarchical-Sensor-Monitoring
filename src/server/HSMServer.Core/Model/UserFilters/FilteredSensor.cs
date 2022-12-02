namespace HSMServer.Core.Model.UserFilters
{
    // TODO: This class should be removed after User refactoring
    // this class is a temporary solution for solving the problem of relating UserExtensions and TreeUserFilter 
    public class FilteredSensor
    {
        public bool IsNotificationsEnabled { get; init; }

        public bool IsNotificationsIgnored { get; init; }

        public SensorStatus Status { get; init; }

        public SensorState State { get; init; }

        public bool HasData { get; init; }
    }
}
