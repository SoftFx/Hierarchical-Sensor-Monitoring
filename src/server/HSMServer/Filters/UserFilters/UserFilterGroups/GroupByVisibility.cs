namespace HSMServer.UserFilters
{
    public class GroupByVisibility : UserFilterGroupBase
    {
        internal override FilterProperty[] Properties => new[] { Empty, SensorsCount, ErrorsCount, Icons };

        internal override FilterGroupType Type => FilterGroupType.ByVisibility;


        public FilterProperty Empty { get; init; } = new("Empty sensors", true);

        public FilterProperty SensorsCount { get; init; } = new("Sensors count");

        public FilterProperty ErrorsCount { get; init; } = new("Errors count");

        public FilterProperty Icons { get; init; } = new("Icons", true);


        public GroupByVisibility() { }


        internal override bool IsSensorSuitable(FilteredSensor sensor) => Empty.Value || sensor.HasData;

        internal override bool NeedToCheckSensor(FilterGroupType mask) => true;
    }
}
