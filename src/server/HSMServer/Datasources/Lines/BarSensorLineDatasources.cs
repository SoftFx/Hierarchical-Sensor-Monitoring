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


    // StdDev (#1509) is unknown for bars from older collectors, rows stored before it existed and
    // bars built from partials. Unknown is not 0, so such bars are left out of the line instead of
    // being drawn at zero (the EMA sources above map null to 0.0).
    public abstract class BarBaseStdDevLineDatasource<TValue> : BaseNumberLineDatasource<TValue, double?, double>
        where TValue : BarBaseValue
    {
        protected override Func<TValue, double?> GetPropertyFactory(PlottedProperty property) => property switch
        {
            PlottedProperty.StdDev => v => GetStdDev(v),

            _ => throw BuildException(property),
        };

        protected override double ConvertToChartType(double? value) => value ?? double.NaN;

        // A timeout row repeats the last bar's fields (GetTimeoutValue); it is not a bar of its own.
        protected override bool IsPlotted(BaseValue value) => value is TValue bar && !bar.IsTimeout && GetStdDev(bar) is not null;


        private static double? GetStdDev(TValue value) => value switch
        {
            BarBaseValue<int> intBar => intBar.StdDev,
            BarBaseValue<double> doubleBar => doubleBar.StdDev,
            _ => null,
        };
    }

    public sealed class IntBarStdDevSource : BarBaseStdDevLineDatasource<IntegerBarValue> { }

    public sealed class DoubleBarStdDevSource : BarBaseStdDevLineDatasource<DoubleBarValue> { }


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