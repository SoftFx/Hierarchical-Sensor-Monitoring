using HSMServer.Core.Model;
using HSMServer.Dashboards;
using System;
using System.Numerics;

namespace HSMServer.Datasources.Aggregators
{
    public sealed class LineDataAggregator<T> : BaseDataAggregator<LinePointState<T>>
        where T : INumber<T>
    {
        private readonly Func<BaseValue, T> _toChartValue;


        internal LineDataAggregator(Func<BaseValue, T> toChartValue)
        {
            _toChartValue = toChartValue;
        }


        protected override BaseChartValue BuildChartValue(BaseValue baseValue) =>
            new LineChartValue<T>(baseValue, _toChartValue(baseValue));

        protected override Func<LinePointState<T>, LinePointState<T>, LinePointState<T>> GetNewStateFactory(PlottedProperty property) =>
            property switch
            {
                PlottedProperty.Max or PlottedProperty.Count => LinePointState<T>.GetMaxState,

                PlottedProperty.Min => LinePointState<T>.GetMinState,

                PlottedProperty.Value or PlottedProperty.Mean or PlottedProperty.EmaValue or PlottedProperty.EmaMin or
                PlottedProperty.EmaMean or PlottedProperty.EmaMax or PlottedProperty.EmaCount => LinePointState<T>.GetAvrState,

                _ => throw new NotImplementedException($"Line aggregation for {property} is not supported")
            };
    }
}