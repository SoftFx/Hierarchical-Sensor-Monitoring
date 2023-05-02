namespace HSMServer.UserFilters
{
    public class GroupByHistory : UserFilterGroupBase
    {
        internal override FilterProperty[] Properties => new[] { Empty };

        internal override FilterGroupType Type => FilterGroupType.ByHistory;


        public FilterProperty Empty { get; init; } = new("No data", true);

        public GroupByHistory() { }


        internal override bool IsSensorSuitable(FilteredSensor sensor) =>
            Empty.Value || sensor.HasData;

        internal override bool NeedToCheckSensor(FilterGroupType mask) =>
            !mask.HasFlag(Type);
    }
}
