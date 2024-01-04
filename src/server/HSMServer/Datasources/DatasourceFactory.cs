using HSMServer.Core.Model;

namespace HSMServer.Datasources
{
    public static class DatasourceFactory
    {
        public static SensorDatasourceBase Build(SensorType type) => type switch
        {
            SensorType.Integer => new LineDatasource<int>(),
            SensorType.Double => new LineDatasource<double>(),
            SensorType.TimeSpan => new TimespanDatasource(),
            SensorType.Boolean => new PointDatasource(),
            SensorType.DoubleBar or SensorType.IntegerBar => new BarsDatasource(),
            _ => new PointDatasource()
        };
    }
}
