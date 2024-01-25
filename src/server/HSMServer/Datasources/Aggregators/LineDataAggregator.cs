using HSMServer.Core.Model;
using System;
using System.Numerics;

namespace HSMServer.Datasources.Aggregators
{
    public sealed class LineDataAggregator<TChart> : BaseDataAggregator
        where TChart : INumber<TChart>
    {
        private Func<BaseValue, TChart> _toChartValue;

        internal LineDataAggregator(Func<BaseValue, TChart> toChartValue)
        {
            _toChartValue = toChartValue;
        }


        protected override BaseChartValue BuildChartValue(BaseValue baseValue) =>
            new LineChartValue<TChart>(baseValue, _toChartValue(baseValue));
    }
}