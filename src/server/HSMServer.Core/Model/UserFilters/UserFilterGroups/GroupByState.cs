namespace HSMServer.Core.Model.UserFilter
{
    public class GroupByState : UserFilterGroupBase
    {
        public override FilterGroupType Type => FilterGroupType.ByState;


        public FilterProperty Blocked { get; init; } = new();


        public GroupByState() { }


        internal override void RegisterProperties()
        {
            RegisterProperty(Blocked);
        }
    }
}
