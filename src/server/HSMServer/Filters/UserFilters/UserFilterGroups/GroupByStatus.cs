using HSMServer.Core.Model;

namespace HSMServer.UserFilters
{
    public sealed class GroupByStatus : UserFilterGroupBase
    {
        internal override FilterProperty[] Properties => new[] { Ok, Warning, Error, OffTime };

        internal override FilterGroupType Type => FilterGroupType.ByStatus;


        public FilterProperty Ok { get; init; } = new(nameof(Ok));

        public FilterProperty Warning { get; init; } = new(nameof(Warning));

        public FilterProperty Error { get; init; } = new(nameof(Error));

        public FilterProperty OffTime { get; init; } = new(nameof(OffTime));

        public GroupByStatus() { }


        public bool IsStatusSuitable(SensorStatus status) =>
            status switch
            {
                SensorStatus.Ok => Ok.Value,
                SensorStatus.Warning => Warning.Value,
                SensorStatus.Error => Error.Value,
                SensorStatus.OffTime => OffTime.Value,
                _ => false
            };

        internal override bool IsSensorSuitable(FilteredSensor sensor) => IsStatusSuitable(sensor.Status);
    }
}
