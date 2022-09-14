namespace HSMServer.Core.Model.UserFilter
{
    public class GroupByNotifications : UserFilterGroupBase
    {
        public override FilterGroupType Type => FilterGroupType.ByNotifications;


        public FilterProperty Enabled { get; init; } = new();

        public FilterProperty Ignored { get; init; } = new();


        public GroupByNotifications() { }

        public GroupByNotifications(GroupByNotifications group)
        {
            Enabled = new(group.Enabled);
            Ignored = new(group.Ignored);
        }


        internal override void RegisterProperties()
        {
            RegisterProperty(Enabled);
            RegisterProperty(Ignored);
        }
    }
}
