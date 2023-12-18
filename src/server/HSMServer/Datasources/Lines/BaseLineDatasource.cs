using HSMServer.Core.Model;
using System;
using System.Numerics;

namespace HSMServer.Datasources
{
    public abstract class BaseLineDatasource<TValue, TProp, TChart> : SensorDatasourceBase
        where TValue : BaseValue
        where TChart : INumber<TChart>
    {
        private BaseChartValue<TChart> _lastVisibleValue;
        protected Func<TValue, TProp> _valueFactory;


        protected override ChartType AggregatedType => ChartType.Line;

        protected override ChartType NormalType => ChartType.Line;


        protected override void AddVisibleValue(BaseValue rawValue)
        {
            if (rawValue is TValue value)
            {
                _lastVisibleValue = new LineChartValue<TChart>(rawValue, GetTargetValue(value));

                AddVisibleToLast(_lastVisibleValue);
            }
        }

        protected override void ApplyToLast(BaseValue rawValue)
        {
            if (rawValue is TValue value)
                _lastVisibleValue.Apply(GetTargetValue(value));
        }


        protected abstract TChart GetTargetValue(TValue value);

        protected Exception BuildException() => new($"Unsupport cast property for {typeof(TValue).Name} {_plotProperty} from {typeof(TProp).Name} to {typeof(TChart).Name}");
    }
}