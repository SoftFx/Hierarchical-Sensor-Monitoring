namespace HSMServer.Core.Model.UserFilter
{
    public sealed class GroupByStatus : UserFilterGroup
    {
        public override FilterGroups Group => FilterGroups.ByStatus;


        public FilterProperty Ok { get; set; } = new();

        public FilterProperty Warning { get; set; } = new();

        public FilterProperty Error { get; set; } = new();

        public FilterProperty Unknown { get; set; } = new();


        public GroupByStatus() { }

        internal GroupByStatus(GroupByStatus group)
        {
            Ok = new FilterProperty(group.Ok);
            Warning = new FilterProperty(group.Warning);
            Error = new FilterProperty(group.Error);
            Unknown = new FilterProperty(group.Unknown);
        }


        internal override void RegisterProperties()
        {
            RegisterProperty(Ok);
            RegisterProperty(Warning);
            RegisterProperty(Error);
            RegisterProperty(Unknown);
        }
    }
}
