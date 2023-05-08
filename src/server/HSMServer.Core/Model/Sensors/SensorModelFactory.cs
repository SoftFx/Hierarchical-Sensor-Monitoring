using HSMDatabase.AccessManager.DatabaseEntities;
using System;

namespace HSMServer.Core.Model
{
    internal static class SensorModelFactory
    {
        internal static BaseSensorModel Build(SensorEntity entity)
        {
            return (SensorType)entity.Type switch
            {
                SensorType.Boolean => new BooleanSensorModel(entity),
                SensorType.Integer => new IntegerSensorModel(entity),
                SensorType.Double => new DoubleSensorModel(entity),
                SensorType.String => new StringSensorModel(entity),
                SensorType.IntegerBar => new IntegerBarSensorModel(entity),
                SensorType.DoubleBar => new DoubleBarSensorModel(entity),
                SensorType.File => new FileSensorModel(entity),
                SensorType.TimeSpan => new TimeSpanSensorModel(entity),
                SensorType.Version => new VersionSensorModel(entity),
                _ => throw new ArgumentException($"Unexpected sensor entity type {entity.Type}"),
            };
        }
    }
}