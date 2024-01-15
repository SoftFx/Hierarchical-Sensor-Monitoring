using System.Collections.Generic;

namespace HSMServer.Datasources
{
    public sealed record InitChartSourceResponse
    {
        public List<object> Values { get; init; }

        public ChartType ChartType { get; init; }
    }


    public sealed record UpdateChartSourceResponse
    {
        public List<object> NewVisibleValues { get; init; }

        public long RemovedValuesCount { get; init; } //todo: can be removed

        public bool IsTimeSpan { get; init; } //todo: should be moved to InitChartSourceResponse
    }
}