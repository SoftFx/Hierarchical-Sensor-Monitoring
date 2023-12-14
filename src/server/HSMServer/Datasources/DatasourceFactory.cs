using HSMServer.Core.Model;
using HSMServer.Dashboards;
using System;

namespace HSMServer.Datasources
{
    public static class DatasourceFactory
    {
        public static SensorDatasourceBase Build(SensorType type, PlottedProperty property) => type switch
        {
            SensorType.Integer when property.IsLineProperty() => new LineDatasource<int>(),

            SensorType.Double when property.IsLineProperty() => new LineDatasource<double>(),

            SensorType.TimeSpan when property.IsLineProperty() => new TimespanDatasource(),

            SensorType.IntegerBar when property.IsLineProperty() => new LineDatasource<int>(),
            SensorType.IntegerBar when property is PlottedProperty.Bar => new BarsDatasource(),

            SensorType.DoubleBar when property.IsLineProperty() => new LineDatasource<double>(),
            SensorType.DoubleBar when property is PlottedProperty.Bar => new BarsDatasource(),

            SensorType.Boolean => new PointDatasource(),

            _ => throw new Exception($"History visualization for {type} sensor is not supported")
        };


        private static bool IsLineProperty(this PlottedProperty property) =>
            property is PlottedProperty.Value or PlottedProperty.Min or PlottedProperty.Mean or PlottedProperty.Max or PlottedProperty.Count;
    }
}