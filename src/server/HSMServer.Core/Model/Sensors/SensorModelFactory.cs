using System;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model.Sensors.SensorModels;
using HSMCommon.Model;
using HSMServer.Core.DataLayer;
using System.Runtime.CompilerServices;

namespace HSMServer.Core.Model
{
    internal static class SensorModelFactory
    {
        internal static BaseSensorModel Build(SensorEntity entity, IDatabaseCore database)
        {
            return (SensorType)entity.Type switch
            {
                SensorType.Boolean => new BooleanSensorModel(entity, database),
                SensorType.Integer => new IntegerSensorModel(entity, database),
                SensorType.Double => new DoubleSensorModel(entity, database),
                SensorType.String => new StringSensorModel(entity, database),
                SensorType.IntegerBar => new IntegerBarSensorModel(entity, database),
                SensorType.DoubleBar => new DoubleBarSensorModel(entity, database),
                SensorType.File => new FileSensorModel(entity, database),
                SensorType.TimeSpan => new TimeSpanSensorModel(entity, database),
                SensorType.Version => new VersionSensorModel(entity, database),
                SensorType.Rate => new RateSensorModel(entity, database),
                SensorType.Enum => new EnumSensorModel(entity, database),
                _ => throw new ArgumentException($"Unexpected sensor entity type {entity.Type}"),
            };
        }
    }
}