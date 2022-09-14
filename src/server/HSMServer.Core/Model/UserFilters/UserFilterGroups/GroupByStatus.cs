namespace HSMServer.Core.Model.UserFilter
{
    public sealed class GroupByStatus : UserFilterGroupBase
    {
        protected override FilterProperty[] Properties => new[] { Ok, Warning, Error, Unknown };

        internal override FilterGroupType Type => FilterGroupType.ByStatus;


        public FilterProperty Ok { get; init; } = new();

        public FilterProperty Warning { get; init; } = new();

        public FilterProperty Error { get; init; } = new();

        public FilterProperty Unknown { get; init; } = new();

        public GroupByStatus() { }
    }
}
