using HSMSensorDataObjects;
using System;

namespace HSMServer.Datasources
{
    public static class DatasourceFactory
    {
        public static SensorDatasourceBase Build(SensorType type) => type switch
        {
            SensorType.IntSensor => new LineDatasource<int>(),
            SensorType.DoubleSensor => new LineDatasource<double>(),
            SensorType.TimeSpanSensor => new LineDatasource<long>(),
            SensorType.BooleanSensor => new PointDatasource(),
            SensorType.DoubleBarSensor or SensorType.IntegerBarSensor => new BarsDatasource(),
            _ => throw new Exception($"History visualization for {type} sensor is not supported")
        };
    }
}
