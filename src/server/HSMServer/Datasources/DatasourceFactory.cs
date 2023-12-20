using HSMServer.Core.Model;
using System;

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
            _ => throw new Exception($"History visualization for {type} sensor is not supported")
        };
    }
}
