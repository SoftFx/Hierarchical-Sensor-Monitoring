namespace HSMServer.Core.Model.UserFilters
{
    public class GroupByState : UserFilterGroupBase
    {
        internal override FilterProperty[] Properties => new[] { Ignored };

        internal override FilterGroupType Type => FilterGroupType.ByState;


        public FilterProperty Ignored { get; init; } = new(nameof(Ignored));

        public GroupByState() { }


        internal override bool IsSensorSuitable(FilteredSensor sensor) =>
            Ignored.Value && sensor.State == SensorState.Ignored;
    }
}
