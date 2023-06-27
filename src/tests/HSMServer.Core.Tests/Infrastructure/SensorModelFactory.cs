using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using System;

namespace HSMServer.Core.Tests.Infrastructure
{
    public static class SensorModelFactory
    {
        public static BaseSensorModel Build(SensorEntity entity)
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

        public static SensorUpdate BuildSensorUpdate(Guid? id = null) =>
            new()
            {
                Id = id ?? Guid.NewGuid(),
                Description = RandomGenerator.GetRandomString(),
                ExpectedUpdateInterval = new(TimeSpan.FromMinutes(10).Ticks),
                State = SensorState.Blocked,
                Integration = Integration.Grafana,
            };
    }
}
