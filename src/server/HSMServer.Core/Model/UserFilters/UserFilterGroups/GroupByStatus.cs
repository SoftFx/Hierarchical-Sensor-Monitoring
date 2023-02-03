namespace HSMServer.Core.Model.UserFilters
{
    public sealed class GroupByStatus : UserFilterGroupBase
    {
        public override FilterProperty[] Properties => new[] { Ok, Warning, Error, OffTime };

        internal override FilterGroupType Type => FilterGroupType.ByStatus;


        public FilterProperty Ok { get; init; } = new(){Name = "Ok"};

        public FilterProperty Warning { get; init; } = new(){Name = "Warning"};

        public FilterProperty Error { get; init; } = new(){Name = "Error"};

        public FilterProperty OffTime { get; init; } = new(){Name = "OffTime"};

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
