using HSMServer.Core.Model;
using HSMServer.Dashboards;
using System;

namespace HSMServer.Datasources.Aggregators
{
    public sealed class VersionDataAggregator : LineDataAggregator<Version, VersionChartValue, VersionPointState>
    {
        protected override VersionPointState BuildState(BaseValue rawValue) =>
            base.BuildState(rawValue).SaveState();

        protected override Func<VersionPointState, VersionPointState, VersionPointState> GetAggrStateFactory(PlottedProperty property)
            => property switch
            {
                PlottedProperty.Value => VersionPointState.GetLastState,

                _ => throw BuildNotSupportedException(property)
            };
    }
}