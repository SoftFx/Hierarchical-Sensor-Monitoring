namespace HSMServer.Core.Model.UserFilter
{
    public sealed class GroupByStatus : UserFilterGroupBase
    {
        public override FilterGroupType Type => FilterGroupType.ByStatus;


        public FilterProperty Ok { get; init; } = new();

        public FilterProperty Warning { get; init; } = new();

        public FilterProperty Error { get; init; } = new();

        public FilterProperty Unknown { get; init; } = new();


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
