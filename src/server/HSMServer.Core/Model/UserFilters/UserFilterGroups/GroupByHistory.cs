namespace HSMServer.Core.Model.UserFilter
{
    public class GroupByHistory : UserFilterGroupBase
    {
        public override FilterGroupType Type => FilterGroupType.ByHistory;


        public FilterProperty Empty { get; init; } = new();


        public GroupByHistory() { }

        public GroupByHistory(GroupByHistory group)
        {
            Empty = new(group.Empty);
        }


        internal override void RegisterProperties()
        {
            RegisterProperty(Empty);
        }
    }
}
