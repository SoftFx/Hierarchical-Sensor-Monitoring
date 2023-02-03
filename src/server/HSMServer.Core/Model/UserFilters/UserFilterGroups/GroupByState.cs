namespace HSMServer.Core.Model.UserFilters
{
    public class GroupByState : UserFilterGroupBase
    {
        public override FilterProperty[] Properties => new[] { Blocked };

        internal override FilterGroupType Type => FilterGroupType.ByState;


        public FilterProperty Blocked { get; init; } = new(){Name = "Blocked"};

        public GroupByState() { }


        internal override bool IsSensorSuitable(FilteredSensor sensor) =>
            Blocked.Value && sensor.State == SensorState.Blocked;
    }
}
