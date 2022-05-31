using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.Entities;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal static class EntitiesFactory
    {
        internal static ProductEntity BuildProductEntity(string name = null, string parent = "") =>
            new()
            {
                Id = Guid.NewGuid().ToString(),
                AuthorId = Guid.NewGuid().ToString(),
                ParentProductId = parent?.Length == 0 ? Guid.NewGuid().ToString() : parent,
                State = (int)ProductState.FullAccess,
                DisplayName = name ?? RandomGenerator.GetRandomString(),
                Description = RandomGenerator.GetRandomString(),
                CreationDate = DateTime.UtcNow.Ticks,
                SubProductsIds = new List<string>(2),
                SensorsIds = new List<string>(2),
            };

        internal static ProductEntity AddSubProduct(this ProductEntity product, string subProductId)
        {
            product.SubProductsIds.Add(subProductId);

            return product;
        }

        internal static ProductEntity AddSensor(this ProductEntity product, string sensorId)
        {
            product.SensorsIds.Add(sensorId);

            return product;
        }


        internal static SensorEntity BuildSensorEntity(string name = null, string parent = "") =>
            new()
            {
                Id = Guid.NewGuid().ToString(),
                ProductId = parent?.Length == 0 ? Guid.NewGuid().ToString() : parent,
                SensorName = name ?? RandomGenerator.GetRandomString(),
                ProductName = RandomGenerator.GetRandomString(),
                Path = RandomGenerator.GetRandomString(),
                Description = RandomGenerator.GetRandomString(),
                SensorType = RandomGenerator.GetRandomByte(),
                ExpectedUpdateIntervalTicks = RandomGenerator.GetRandomInt(),
                Unit = RandomGenerator.GetRandomString(),
            };


        internal static SensorDataEntity BuildSensorDataEntity(string path, byte type) =>
            new()
            {
                Status = RandomGenerator.GetRandomByte(),
                Path = path ?? RandomGenerator.GetRandomString(),
                Time = DateTime.UtcNow.AddDays(-1),
                TimeCollected = DateTime.UtcNow,
                Timestamp = DateTime.UtcNow.AddDays(-1).GetTimestamp(),
                TypedData = RandomGenerator.GetRandomString(),
                DataType = type,
                OriginalFileSensorContentSize = RandomGenerator.GetRandomInt(),
            };
    }
}
