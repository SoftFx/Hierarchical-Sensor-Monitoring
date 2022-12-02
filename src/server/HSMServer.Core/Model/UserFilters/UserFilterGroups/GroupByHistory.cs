namespace HSMServer.Core.Model.UserFilters
{
    public class GroupByHistory : UserFilterGroupBase
    {
        protected override FilterProperty[] Properties => new[] { Empty };

        internal override FilterGroupType Type => FilterGroupType.ByHistory;


        public FilterProperty Empty { get; init; } = new();

        public GroupByHistory() { }


        internal override bool IsSensorSuitable(FilteredSensor sensor) =>
            Empty.Value || sensor.HasData;

        internal override bool NeedToCheckSensor(FilterGroupType mask) =>
            !mask.HasFlag(Type);
    }
}
