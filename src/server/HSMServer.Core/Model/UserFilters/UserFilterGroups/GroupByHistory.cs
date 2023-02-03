namespace HSMServer.Core.Model.UserFilters
{
    public class GroupByHistory : UserFilterGroupBase
    {
        public override FilterProperty[] Properties => new[] { Empty };

        internal override FilterGroupType Type => FilterGroupType.ByHistory;


        public FilterProperty Empty { get; init; } = new(){Name = "No data"};

        public GroupByHistory() { }


        internal override bool IsSensorSuitable(FilteredSensor sensor) =>
            Empty.Value || sensor.HasData;

        internal override bool NeedToCheckSensor(FilterGroupType mask) =>
            !mask.HasFlag(Type);
    }
}
