using HSMServer.Core.Model;
using HSMServer.Dashboards;
using System;

namespace HSMServer.Datasources
{
    public static class DatasourceFactory
    {
        public static SensorDatasourceBase Build(BaseSensorModel sensor, PlottedProperty property)
        {
            SensorDatasourceBase source = sensor.Type switch
            {
                SensorType.Integer when property.IsInstantView() => new IntLineDatasource<int>(),
                SensorType.Integer when property.IsInstantDoubleLine() => new IntLineDatasource<double>(),

                SensorType.Double when property.IsInstantView() => new DoubleLineDatasource<double>(),
                SensorType.Double when property.IsInstantDoubleLine() => new DoubleLineDatasource<double>(),

                SensorType.TimeSpan when property.IsInstantView() => new TimespanDatasource(),
                //SensorType.TimeSpan when property.IsInstantView() => new TimespanDatasource(), //EMA for timespan

                SensorType.IntegerBar when property.IsBarView() => new BarsDatasource(),
                SensorType.IntegerBar when property.IsBarLine() => new IntBarLineDatasource<int>(),
                SensorType.IntegerBar when property.IsBarIntLine() => new IntBarLineDatasource<int>(),
                SensorType.IntegerBar when property.IsBarDoubleLine() => new IntBarLineDatasource<double>(),

                SensorType.DoubleBar when property.IsBarView() => new BarsDatasource(),
                SensorType.DoubleBar when property.IsBarLine() => new DoubleBarLineDatasource<double>(),
                SensorType.DoubleBar when property.IsBarIntLine() => new DoubleBarLineDatasource<int>(),
                SensorType.DoubleBar when property.IsBarDoubleLine() => new DoubleBarLineDatasource<double>(),

                SensorType.Boolean => new PointDatasource(),

                _ => throw new Exception($"History visualization for {sensor.Type} sensor is not supported")
            };

            return source.AttachSensor(sensor, property);
        }


        private static bool IsInstantView(this PlottedProperty property) => property is PlottedProperty.Value;

        private static bool IsInstantDoubleLine(this PlottedProperty property) => property is PlottedProperty.EmaValue;


        private static bool IsBarView(this PlottedProperty property) => property is PlottedProperty.Bar;

        private static bool IsBarLine(this PlottedProperty property) => property is PlottedProperty.Min or PlottedProperty.Mean or PlottedProperty.Max;

        private static bool IsBarIntLine(this PlottedProperty property) => property is PlottedProperty.Count or PlottedProperty.EmaCount;

        private static bool IsBarDoubleLine(this PlottedProperty property) => property is PlottedProperty.EmaMin or PlottedProperty.Mean or PlottedProperty.Max;


        private static Exception BuildException<T>(PlottedProperty property) => new($"Invalid property {property} for {typeof(T).FullName}");
    }
}