namespace HSMServer.Core.Model.UserFilter
{
    public class GroupByHistory : UserFilterGroupBase
    {
        protected override FilterProperty[] Properties => new[] { Empty };

        internal override FilterGroupType Type => FilterGroupType.ByHistory;


        public FilterProperty Empty { get; init; } = new();

        public GroupByHistory() { }
    }
}
