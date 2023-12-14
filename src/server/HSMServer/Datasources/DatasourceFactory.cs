using HSMServer.Core.Model;
using HSMServer.Dashboards;
using System;
using System.Numerics;

namespace HSMServer.Datasources
{
    public static class DatasourceFactory
    {
        public static SensorDatasourceBase Build(BaseSensorModel sensor, PlottedProperty property)
        {
            SensorDatasourceBase source = sensor.Type switch
            {
                SensorType.Integer when property.IsInstantView() => new LineDatasource<int>(),

                SensorType.Double when property.IsInstantView() => new LineDatasource<double>(),

                SensorType.TimeSpan when property.IsInstantView() => new TimespanDatasource(),

                SensorType.IntegerBar when property.IsBarView() => new BarsDatasource(),
                SensorType.IntegerBar when property.IsBarLine() => new LineDatasource<int>(),
                SensorType.IntegerBar when property.IsBarIntLine() => new LineDatasource<int>(),

                SensorType.DoubleBar when property.IsBarView() => new BarsDatasource(),
                SensorType.DoubleBar when property.IsBarLine() => new LineDatasource<double>(),
                SensorType.DoubleBar when property.IsBarIntLine() => new LineDatasource<int>(),

                SensorType.Boolean => new PointDatasource(),

                _ => throw new Exception($"History visualization for {sensor.Type} sensor is not supported")
            };

            return source.AttachSensor(sensor, property);
        }


        public static Func<BaseValue, T> GetNumberValueFactory<T>(PlottedProperty property) where T : struct, INumber<T> =>
            property switch
            {
                PlottedProperty.Value => v => ((BaseValue<T>)v).Value,
                PlottedProperty.Min => v => ((BarBaseValue<T>)v).Min,
                PlottedProperty.Max => v => ((BarBaseValue<T>)v).Max,
                PlottedProperty.Mean => v => ((BarBaseValue<T>)v).Mean,
                _ => throw BuildException<T>(property)
            };


        public static Func<BaseValue, int> GetIntValueFactory<T>(PlottedProperty property) where T : struct, INumber<T> =>
            property switch
            {
                PlottedProperty.Count => v => ((BarBaseValue<T>)v).Count
            };


        private static bool IsInstantView(this PlottedProperty property) => property is PlottedProperty.Value;


        private static bool IsBarView(this PlottedProperty property) => property is PlottedProperty.Bar;

        private static bool IsBarLine(this PlottedProperty property) => property is PlottedProperty.Min or PlottedProperty.Mean or PlottedProperty.Max;

        private static bool IsBarIntLine(this PlottedProperty property) => property is PlottedProperty.Count;


        private static Exception BuildException<T>(PlottedProperty property) => new Exception($"Invalid property {property} for {typeof(T).FullName}");
    }
}