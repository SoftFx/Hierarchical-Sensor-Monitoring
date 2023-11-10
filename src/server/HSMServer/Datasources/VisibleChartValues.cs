using System;
using System.Numerics;

namespace HSMServer.Datasources
{
    public abstract record BaseChartValue
    {
        public DateTime Time { get; init; }

        public string Tooltip { get; init; }
    }


    public sealed record LineChartValue<T> : BaseChartValue where T: INumber<T>
    {
        public T Value { get; init; }
    }
}