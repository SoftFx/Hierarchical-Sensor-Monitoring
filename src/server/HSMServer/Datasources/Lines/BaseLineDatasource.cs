using HSMServer.Core.Model;
using HSMServer.Dashboards;
using HSMServer.Datasources.Aggregators;
using System;
using System.Numerics;

namespace HSMServer.Datasources
{
    public abstract class BaseLineDatasource<TValue, TProp, TChart> : SensorDatasourceBase
        where TValue : BaseValue
        where TChart : INumber<TChart>
    {
        private BaseChartValue<TChart> _lastVisibleValue;
        protected Func<TValue, TProp> _getPropertyFactory;


        protected override BaseDataAggregator DataAggregator { get; }


        protected override ChartType AggregatedType => ChartType.Line;

        protected override ChartType NormalType => ChartType.Line;


        protected BaseLineDatasource()
        {
            DataAggregator = new LineDataAggregator<TChart>(ToChartValue);
        }

        internal override SensorDatasourceBase AttachSensor(BaseSensorModel sensor, SourceSettings settings)
        {
            base.AttachSensor(sensor, settings);

            _getPropertyFactory = GetPropertyFactory(settings.Property);

            return this;
        }


        //protected override void AddVisibleValue(BaseValue rawValue)
        //{
        //    if (rawValue is TValue value)
        //    {
        //        //if (_isBarSensor)
        //        //    _lastBarValue = rawValue as BarBaseValue;

        //        _lastVisibleValue = new LineChartValue<TChart>(rawValue, ToChartValue(value));

        //        AddVisibleToLast(_lastVisibleValue);
        //    }
        //}

        protected override void ApplyToLast(BaseValue rawValue)
        {
            if (rawValue is TValue value)
            {
                var chartValue = ToChartValue(value);

                //if (IsPartialValueUpdate(value))
                //    _lastVisibleValue.ReapplyLast(chartValue, value.Time);
                //else
                _lastVisibleValue.Apply(chartValue, value.Time);
            }
        }


        protected abstract Func<TValue, TProp> GetPropertyFactory(PlottedProperty property);

        protected abstract TChart ConvertToChartType(TProp value);


        protected Exception BuildException(PlottedProperty property) => new($"Unsupport cast property for {typeof(TValue).Name} {property} from {typeof(TProp).Name} to {typeof(TChart).Name}");


        private TChart ToChartValue(BaseValue value) => ConvertToChartType(_getPropertyFactory((TValue)value));
    }
}