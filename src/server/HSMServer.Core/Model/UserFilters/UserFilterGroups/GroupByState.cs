namespace HSMServer.Core.Model.UserFilter
{
    public class GroupByState : UserFilterGroupBase
    {
        public override FilterGroupType Type => FilterGroupType.ByState;


        public FilterProperty Blocked { get; init; } = new();


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
