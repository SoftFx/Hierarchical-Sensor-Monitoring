namespace HSMServer.Core.Model.UserFilters
{
    public class GroupByState : UserFilterGroupBase
    {
        protected override FilterProperty[] Properties => new[] { Blocked };

        internal override FilterGroupType Type => FilterGroupType.ByState;


        public FilterProperty Blocked { get; init; } = new();

        public GroupByState() { }


        internal override bool IsSensorSuitable(FilteredSensor sensor) =>
            Blocked.Value && sensor.State == SensorState.Blocked;
    }
}
