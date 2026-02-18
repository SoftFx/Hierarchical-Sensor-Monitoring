using System;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Sensors.SensorModels;
using HSMCommon.Model;
using HSMServer.Core.DataLayer;

namespace HSMServer.Core.Tests.Infrastructure
{
    public static class SensorModelFactory
    {
        public static BaseSensorModel Build(SensorEntity entity)
        {
            return (SensorType)entity.Type switch
            {
                SensorType.Boolean => new BooleanSensorModel(entity, null),
                SensorType.Integer => new IntegerSensorModel(entity, null),
                SensorType.Double => new DoubleSensorModel(entity, null),
                SensorType.Rate => new RateSensorModel(entity, null),
                SensorType.String => new StringSensorModel(entity, null),
                SensorType.IntegerBar => new IntegerBarSensorModel(entity, null),
                SensorType.DoubleBar => new DoubleBarSensorModel(entity, null),
                SensorType.File => new FileSensorModel(entity, null),
                SensorType.TimeSpan => new TimeSpanSensorModel(entity, null),
                SensorType.Version => new VersionSensorModel(entity, null),
                SensorType.Enum => new EnumSensorModel(entity, null),
                _ => throw new ArgumentException($"Unexpected sensor entity type {entity.Type}"),
            };
        }

        public static SensorUpdate BuildSensorUpdate(Guid? id = null) =>
            new()
            {
                Id = id ?? Guid.NewGuid(),
                Description = RandomGenerator.GetRandomString(),
                TTL = new(TimeSpan.FromMinutes(10).Ticks),
                State = SensorState.Blocked,
                Integration = Integration.Grafana,
            };
    }
}
