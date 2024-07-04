using HSMServer.Dashboards;
using System;
using System.Numerics;

namespace HSMServer.Datasources.Aggregators
{
    public sealed class LineNumberDataAggregator<T> : LineDataAggregator<T, LineChartValue<T>, LineNumberPointState<T>>
        where T : INumber<T>
    {
        protected override Func<LineNumberPointState<T>, LineNumberPointState<T>, LineNumberPointState<T>> GetAggrStateFactory(PlottedProperty property) =>
            property switch
            {
                PlottedProperty.Max or PlottedProperty.Count => LineNumberPointState<T>.GetMaxState,

                PlottedProperty.Min => LineNumberPointState<T>.GetMinState,

                PlottedProperty.Value or PlottedProperty.Mean or PlottedProperty.EmaValue or PlottedProperty.EmaMin or
                PlottedProperty.EmaMean or PlottedProperty.EmaMax or PlottedProperty.EmaCount => LineNumberPointState<T>.GetAvrState,

                _ => throw BuildNotSupportedException(property),
            };
    }
}