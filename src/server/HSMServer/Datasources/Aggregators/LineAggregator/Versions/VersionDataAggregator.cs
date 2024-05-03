using HSMServer.Core.Model;
using HSMServer.Dashboards;
using System;

namespace HSMServer.Datasources.Aggregators
{
    public sealed class VersionDataAggregator : BaseDataAggregator<VersionPointState>
    {
        private readonly Func<BaseValue, Version> _toChartValue;


        internal VersionDataAggregator(Func<BaseValue, Version> toChartValue)
        {
            _toChartValue = toChartValue;
        }


        protected override void ApplyState(BaseChartValue point, VersionPointState state)
        {
            throw new NotImplementedException();
        }

        protected override BaseChartValue BuildNewPoint()
        {
            throw new NotImplementedException();
        }

        protected override VersionPointState BuildState(BaseValue value)
        {
            throw new NotImplementedException();
        }

        protected override Func<VersionPointState, VersionPointState, VersionPointState> GetAggrStateFactory(PlottedProperty property)
        {
            throw new NotImplementedException();
        }
    }
}