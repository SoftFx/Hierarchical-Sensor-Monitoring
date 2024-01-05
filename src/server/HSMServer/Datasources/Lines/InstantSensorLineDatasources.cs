using HSMServer.Core.Model;
using HSMServer.Dashboards;
using System;
using System.Numerics;

namespace HSMServer.Datasources
{
    public abstract class InstantBaseLineDatasource<TValue, TProp, TChart> : BaseLineDatasource<TValue, TProp, TChart>
            where TValue : BaseValue<TProp>
            where TChart : INumber<TChart>
    {
        protected override Func<TValue, TProp> GetPropertyFactory() => _plotProperty switch
        {
            PlottedProperty.Value => v => v.Value,

            _ => throw BuildException(),
        };
    }

    public sealed class IntLineDatasource : InstantBaseLineDatasource<IntegerValue, int, int>
    {
        protected override int ConvertToChartType(int value) => value;
    }

    public sealed class DoubleLineDatasource : InstantBaseLineDatasource<DoubleValue, double, double>
    {
        protected override double ConvertToChartType(double value) => value;
    }

    public sealed class TimespanLineDatasource : InstantBaseLineDatasource<TimeSpanValue, TimeSpan, long>
    {
        protected override long ConvertToChartType(TimeSpan value) => value.Ticks;
    }


    public abstract class InstantBaseNullDoubleLineDatasource<TValue> : BaseLineDatasource<TValue, double?, double>
       where TValue : BaseInstantValue
    {
        protected override Func<TValue, double?> GetPropertyFactory() => _plotProperty switch
        {
            PlottedProperty.EmaValue => v => v.EmaValue,

            _ => throw BuildException(),
        };

        protected override double ConvertToChartType(double? value) => value ?? 0.0;
    }

    public sealed class IntToNullDoubleLineDatasource : InstantBaseNullDoubleLineDatasource<IntegerValue> { }

    public sealed class DoubleToNullDoubleDatasource : InstantBaseNullDoubleLineDatasource<DoubleValue> { }
}
