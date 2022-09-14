namespace HSMServer.Core.Model.UserFilter
{
    public class GroupByNotifications : UserFilterGroupBase
    {
        protected override FilterProperty[] Properties => new[] { Enabled, Ignored };

        internal override FilterGroupType Type => FilterGroupType.ByNotifications;


        public FilterProperty Enabled { get; init; } = new();

        public FilterProperty Ignored { get; init; } = new();


        public GroupByNotifications() { }
    }
}
