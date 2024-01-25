using HSMServer.Core.Cache;
using HSMServer.Dashboards;
using System;

namespace HSMServer.Datasources
{
    public sealed record SourceSettings
    {
        private const int DefaultMaxVisibleCnt = 100;


        public PlottedProperty Property { get; init; }

        public bool AggregateValues { get; init; }

        public int CustomVisibleCount { get; init; } = DefaultMaxVisibleCnt;

        public int MaxVisibleCount => AggregateValues ? Math.Max(CustomVisibleCount, TreeValuesCache.MaxHistoryCount) : int.MaxValue;
    }
}