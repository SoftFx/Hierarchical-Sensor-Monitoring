namespace HSMServer.Core.Model.UserFilter
{
    public class GroupByHistory : UserFilterGroup
    {
        public override FilterGroups Group => FilterGroups.ByHistory;


        public FilterProperty Empty { get; set; } = new();


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
