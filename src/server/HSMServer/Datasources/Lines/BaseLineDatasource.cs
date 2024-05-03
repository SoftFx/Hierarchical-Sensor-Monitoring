using HSMServer.Core.Model;
using HSMServer.Dashboards;
using HSMServer.Datasources.Aggregators;
using System;

namespace HSMServer.Datasources.Lines
{
    public abstract class BaseLineDatasource<TValue, TProp, TChart> : SensorDatasourceBase
        where TValue : BaseValue
    {
        protected Func<TValue, TProp> _getPropertyFactory;


        protected override ChartType AggregatedType => ChartType.Line;

        protected override ChartType NormalType => ChartType.Line;


        protected BaseLineDatasource()
        {
            TChart ToChartValue(BaseValue value) => ConvertToChartType(_getPropertyFactory((TValue)value));

            if (DataAggregator is IDataAggregator<TChart> valueAggregator)
                valueAggregator.AttachConverter(ToChartValue);
        }


        internal override SensorDatasourceBase AttachSensor(BaseSensorModel sensor, SourceSettings settings)
        {
            base.AttachSensor(sensor, settings);

            _getPropertyFactory = GetPropertyFactory(settings.Property);

            return this;
        }


        protected abstract Func<TValue, TProp> GetPropertyFactory(PlottedProperty property);

        protected abstract TChart ConvertToChartType(TProp value);


        protected static Exception BuildException(PlottedProperty property) => new($"Unsupport cast property for {typeof(TValue).Name} {property} from {typeof(TProp).Name} to {typeof(TChart).Name}");

        protected static Func<T, P> GetValuePropertyFactory<T, P>(PlottedProperty property) where T : BaseValue<P> =>
            property switch
            {
                PlottedProperty.Value => v => v.Value,

                _ => throw BuildException(property),
            };
    }
}