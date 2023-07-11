using HSMServer.Core.Model;

namespace HSMServer.UserFilters
{
    public class GroupByState : UserFilterGroupBase
    {
        internal override FilterProperty[] Properties => new[] { Muted };

        internal override FilterGroupType Type => FilterGroupType.ByState;


        public FilterProperty Muted { get; init; } = new(nameof(Muted));

        public GroupByState() { }


        internal override bool IsSensorSuitable(FilteredSensor sensor) =>
            Muted.Value && sensor.State == SensorState.Muted;
    }
}
