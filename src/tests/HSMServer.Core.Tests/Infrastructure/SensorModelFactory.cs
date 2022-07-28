using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.Entities;
using HSMServer.Core.Model;
using System;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal static class SensorModelFactory
    {
        internal static BaseSensorModel Build(SensorEntity entity)
        {
            BaseSensorModel BuildSensor<T>() where T : BaseSensorModel, new() =>
                new T().ApplyEntity(entity);

            return (SensorType)entity.Type switch
            {
                SensorType.Boolean => BuildSensor<BooleanSensorModel>(),
                SensorType.Integer => BuildSensor<IntegerSensorModel>(),
                SensorType.Double => BuildSensor<DoubleSensorModel>(),
                SensorType.String => BuildSensor<StringSensorModel>(),
                SensorType.IntegerBar => BuildSensor<IntegerBarSensorModel>(),
                SensorType.DoubleBar => BuildSensor<DoubleBarSensorModel>(),
                SensorType.File => BuildSensor<FileSensorModel>(),
                _ => throw new ArgumentException($"Unexpected sensor entity type {entity.Type}"),
            };
        }

        internal static SensorUpdate BuildSensorUpdate(Guid? id = null) =>
            new()
            {
                Id = id ?? Guid.NewGuid(),
                Description = RandomGenerator.GetRandomString(),
                ExpectedUpdateInterval = TimeSpan.FromMinutes(10),
                Unit = RandomGenerator.GetRandomString(),
            };
    }
}
