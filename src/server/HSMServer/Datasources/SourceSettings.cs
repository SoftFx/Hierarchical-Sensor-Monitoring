using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Dashboards;
using System;

namespace HSMServer.Datasources
{
    public sealed record SourceSettings
    {
        private const int DefaultMaxVisibleCnt = 100;


        public required PlottedProperty Property { get; init; }

        public required SensorType SensorType { get; init; }


        public bool AggregateValues { get; init; }

        public int CustomVisibleCount { get; init; } = DefaultMaxVisibleCnt;

        public int MaxVisibleCount => AggregateValues ? Math.Min(CustomVisibleCount, TreeValuesCache.MaxHistoryCount) : int.MaxValue;
    }
}