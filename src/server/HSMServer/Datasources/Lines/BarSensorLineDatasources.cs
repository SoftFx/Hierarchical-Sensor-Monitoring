using System;
using System.Numerics;
using HSMCommon.Model;
using HSMServer.Dashboards;


namespace HSMServer.Datasources
{
    public abstract class BarBaseLineDatasource<TValue, TProp, TChart> : BaseNumberLineDatasource<TValue, TProp, TChart>
        where TValue : BarBaseValue<TProp>
        where TProp : struct, INumber<TProp>
        where TChart : INumber<TChart>
    {
        protected override Func<TValue, TProp> GetPropertyFactory(PlottedProperty property) => property switch
        {
            PlottedProperty.Min => v => v.Min,
            PlottedProperty.Max => v => v.Max,
            PlottedProperty.Mean => v => v.Mean,

            PlottedProperty.FirstValue => v => v.FirstValue ?? v.Min,
            PlottedProperty.LastValue => v => v.LastValue,

            _ => throw BuildException(property),
        };

        protected override TChart ConvertToChartType(TProp value) => TChart.CreateChecked(value);
    }

    public sealed class IntBarLineDatasource : BarBaseLineDatasource<IntegerBarValue, int, int> { }

    public sealed class DoubleBarLineDatasource : BarBaseLineDatasource<DoubleBarValue, double, double> { }


    public abstract class BarBaseNullDoubleLineDatasource<TValue> : BaseNumberLineDatasource<TValue, double?, double>
        where TValue : BarBaseValue
    {
        protected override Func<TValue, double?> GetPropertyFactory(PlottedProperty property) => property switch
        {
            PlottedProperty.EmaMin => v => v.EmaMin,
            PlottedProperty.EmaMax => v => v.EmaMax,
            PlottedProperty.EmaMean => v => v.EmaMean,
            PlottedProperty.EmaCount => v => v.EmaCount,

            _ => throw BuildException(property),
        };

        protected override double ConvertToChartType(double? value) => value ?? 0.0;
    }

    public sealed class IntBarNullDoubleSource : BarBaseNullDoubleLineDatasource<IntegerBarValue> { }

    public sealed class DoubleBarNullDoubleSource : BarBaseNullDoubleLineDatasource<DoubleBarValue> { }


    public abstract class BarBaseIntLineDatasource<TValue> : BaseNumberLineDatasource<TValue, int, int>
        where TValue : BarBaseValue
    {
        protected override Func<TValue, int> GetPropertyFactory(PlottedProperty property) => property switch
        {
            PlottedProperty.Count => v => v.Count,

            _ => throw BuildException(property),
        };

        protected override int ConvertToChartType(int value) => value;
    }

    public sealed class IntBarIntLineSource : BarBaseIntLineDatasource<IntegerBarValue> { }

    public sealed class DoubleBarIntLineSource : BarBaseIntLineDatasource<DoubleBarValue> { }
}