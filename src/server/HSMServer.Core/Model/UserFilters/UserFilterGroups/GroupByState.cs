namespace HSMServer.Core.Model.UserFilter
{
    public class GroupByState : UserFilterGroupBase
    {
        protected override FilterProperty[] Properties => new[] { Blocked };

        internal override FilterGroupType Type => FilterGroupType.ByState;


        public FilterProperty Blocked { get; init; } = new();

        public GroupByState() { }
    }
}
