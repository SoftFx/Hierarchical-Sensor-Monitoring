using HSMServer.Dashboards;

namespace HSMServer.Datasources
{
    public sealed record SourceSettings
    {
        private const int DefaultMaxVisibleCnt = 100;


        public PlottedProperty Property { get; init; }

        public bool NotAggregateValues { get; init; }

        public int CustomVisibleCount { get; init; } = DefaultMaxVisibleCnt;

        public int MaxVisibleCount => NotAggregateValues ? int.MaxValue : CustomVisibleCount;
    }
}