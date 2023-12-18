using HSMServer.Core.Model;
using HSMServer.Dashboards;
using System;
using System.Numerics;

namespace HSMServer.Datasources
{
    public abstract class BaseLineDatasource<TSensor, TBase, TProp> : SensorDatasourceBase
        where TSensor : BaseValue
        where TProp : INumber<TProp>
    {
        private BaseChartValue<TProp> _lastVisibleValue;
        protected Func<TSensor, TBase> _valueFactory;


        protected override ChartType AggregatedType => ChartType.Line;

        protected override ChartType NormalType => ChartType.Line;


        protected override void AddVisibleValue(BaseValue rawValue)
        {
            if (rawValue is TSensor value)
            {
                _lastVisibleValue = new LineChartValue<TProp>(rawValue, GetTargetValue(value));

                AddVisibleToLast(_lastVisibleValue);
            }
        }

        protected override void ApplyToLast(BaseValue rawValue)
        {
            if (rawValue is TSensor value)
                _lastVisibleValue.Apply(GetTargetValue(value));
        }


        protected abstract TProp GetTargetValue(TSensor value);

        protected Exception BuildException() => new($"Unsupport cast property for {typeof(TSensor).Name} {_plotProperty} from {typeof(TBase).Name} to {typeof(TProp).Name}");
    }


    public abstract class InstantBaseLineDatasource<TSensor, TBase, TProp> : BaseLineDatasource<TSensor, TBase, TProp>
        where TSensor : BaseValue<TBase>
        where TBase : INumber<TBase>
        where TProp : INumber<TProp>
    {
        internal InstantBaseLineDatasource()
        {
            _valueFactory = _plotProperty switch
            {
                PlottedProperty.Value => v => v.Value,

                _ => throw BuildException(),
            };
        }


        protected override TProp GetTargetValue(TSensor value) => TProp.CreateChecked(_valueFactory(value));
    }


    public abstract class BarBaseLineDatasource<TSensor, TBase, TProp> : BaseLineDatasource<TSensor, TBase, TProp>
        where TSensor : BarBaseValue<TBase>
        where TBase : struct, INumber<TBase>
        where TProp : INumber<TProp>
    {
        internal BarBaseLineDatasource()
        {
            _valueFactory = _plotProperty switch
            {
                PlottedProperty.Min => v => v.Min,
                PlottedProperty.Max => v => v.Max,
                PlottedProperty.Mean => v => v.Mean,

                _ => throw BuildException(),
            };
        }


        protected override TProp GetTargetValue(TSensor value) => TProp.CreateChecked(_valueFactory(value));
    }


    public abstract class BarBaseNullLineDatasource<TSensor, TBase> : BaseLineDatasource<TSensor, double?, double>
        where TSensor : BarBaseValue
        where TBase : struct, INumber<TBase>
    {
        internal BarBaseNullLineDatasource()
        {
            _valueFactory = _plotProperty switch
            {
                PlottedProperty.EmaMin => v => v.EmaMin,
                PlottedProperty.EmaMax => v => v.EmaMax,
                PlottedProperty.EmaMean => v => v.EmaMean,
                PlottedProperty.EmaCount => v => v.EmaCount,

                _ => throw BuildException(),
            };
        }
    }



    public sealed class IntLineDatasource<TProp> : InstantBaseLineDatasource<IntegerValue, int, TProp>
        where TProp : INumber<TProp>
    { }


    public sealed class DoubleLineDatasource<TProp> : InstantBaseLineDatasource<DoubleValue, double, TProp>
        where TProp : INumber<TProp>
    { }


    public sealed class IntBarLineDatasource<TProp> : BarBaseLineDatasource<IntegerBarValue, int, TProp>
        where TProp : INumber<TProp>
    { }  


    public sealed class DoubleBarLineDatasource<TProp> : BarBaseLineDatasource<DoubleBarValue, double, TProp>
        where TProp : INumber<TProp>
    { }


    public sealed class IntBarNullDoubleSource : BarBaseLineDatasource<IntegerBarValue, int, double?> { }
}
