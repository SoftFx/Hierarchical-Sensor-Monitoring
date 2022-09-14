namespace HSMServer.Core.Model.UserFilter
{
    public class GroupByNotifications : UserFilterGroup
    {
        public override FilterGroups Group => FilterGroups.ByNotifications;


        public FilterProperty Enabled { get; set; } = new();

        public FilterProperty Ignored { get; set; } = new();


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
