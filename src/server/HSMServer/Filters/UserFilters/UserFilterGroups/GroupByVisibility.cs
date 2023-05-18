namespace HSMServer.UserFilters
{
    public class GroupByVisibility : UserFilterGroupBase
    {
        internal override FilterProperty[] Properties => new[] { Empty, Icons };

        internal override FilterGroupType Type => FilterGroupType.ByVisibility;


        public FilterProperty Empty { get; init; } = new("Empty sensors", true);
        
        public FilterProperty Icons { get; init; } = new("Icons", true);

        public GroupByVisibility() { }


        internal override bool IsSensorSuitable(FilteredSensor sensor) =>
            Empty.Value || sensor.HasData;
    }
}
