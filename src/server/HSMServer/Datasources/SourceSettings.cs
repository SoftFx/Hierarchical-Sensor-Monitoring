using System;
using HSMCommon.Model;
using HSMServer.Core.Cache;
using HSMServer.Dashboards;


namespace HSMServer.Datasources
{
    public sealed record SourceSettings
    {
        private const int DefaultMaxVisibleCnt = 100;
        private const int MaxNotAggrPoints = 1500;

        public required PanelRangeSettings YRange { get; init; }

        public required PlottedProperty Property { get; init; }

        public required SensorType SensorType { get; init; }


        public bool AggregateValues { get; init; }
        
        public bool IsSingleMode { get; init; }

        public int CustomVisibleCount { get; init; } = DefaultMaxVisibleCnt;

        public int MaxVisibleCount => AggregateValues ? Math.Min(CustomVisibleCount, TreeValuesCache.MaxHistoryCount) : MaxNotAggrPoints;

        public bool IsDefaultFilter => !YRange.IsRangeScalePossible(SensorType) || YRange.AutoScale;
    }
}