using HSMServer.Dashboards;
using System;

namespace HSMServer.Datasources.Aggregators
{
    public sealed class VersionDataAggregator : LineDataAggregator<Version, VersionChartValue, VersionPointState>
    {
        protected override Func<VersionPointState, VersionPointState, VersionPointState> GetAggrStateFactory(PlottedProperty property) => property switch
        {
            _ => throw BuildNotSupportedException(property)
        };
    }
}