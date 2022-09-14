namespace HSMServer.Core.Model.UserFilter
{
    public class GroupByState : UserFilterGroup
    {
        public override FilterGroups Group => FilterGroups.ByState;


        public FilterProperty Blocked { get; set; } = new();


        public GroupByState() { }

        public GroupByState(GroupByState group)
        {
            Blocked = new FilterProperty(group.Blocked);
        }


        internal override void RegisterProperties()
        {
            RegisterProperty(Blocked);
        }
    }
}
