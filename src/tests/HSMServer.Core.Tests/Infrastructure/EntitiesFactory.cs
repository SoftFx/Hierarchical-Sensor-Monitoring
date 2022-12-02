using HSMCommon;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal static class EntitiesFactory
    {
        internal static ProductEntity BuildProduct() =>
            BuildProductEntity().AddSubProduct(Guid.NewGuid().ToString());

        internal static ProductEntity BuildProductEntity(string name = null) =>
            new()
            {
                Id = Guid.NewGuid().ToString(),
                AuthorId = Guid.NewGuid().ToString(),
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


        internal static AccessKeyEntity BuildAccessKeyEntity(string id = null, string name = null, string productId = null,
            KeyPermissions permissions = KeyPermissions.CanSendSensorData | KeyPermissions.CanAddNodes | KeyPermissions.CanAddSensors | KeyPermissions.CanReadSensorData) =>
            new()
            {
                Id = id ?? Guid.NewGuid().ToString(),
                AuthorId = Guid.NewGuid().ToString(),
                ProductId = productId ?? Guid.NewGuid().ToString(),
                State = (byte)KeyState.Active,
                Permissions = (long)permissions,
                DisplayName = name ?? RandomGenerator.GetRandomString(),
                CreationTime = DateTime.UtcNow.Ticks,
                ExpirationTime = DateTime.MaxValue.Ticks
            };


        internal static SensorEntity BuildSensorEntity(string name = null, byte? type = null) =>
            new()
            {
                Id = Guid.NewGuid().ToString(),
                DisplayName = name ?? RandomGenerator.GetRandomString(),
                Description = RandomGenerator.GetRandomString(),
                Type = type ?? RandomGenerator.GetRandomByte(),
                Unit = RandomGenerator.GetRandomString(),
            };


        internal static User BuildUser() => new()
        {
            UserName = RandomGenerator.GetRandomString(),
            Password = HashComputer.ComputePasswordHash(RandomGenerator.GetRandomString()),
            IsAdmin = false
        };


        internal static RegistrationTicket BuildTicket() => new()
        {
            Role = nameof(ProductRoleEnum.ProductManager),
            ExpirationDate = DateTime.UtcNow.AddMinutes(30),
            ProductKey = Guid.NewGuid().ToString()
        };


        internal static ConfigurationObject BuildConfiguration(string name) => new()
        {
            Name = name,
            Value = RandomGenerator.GetRandomString(),
            Description = RandomGenerator.GetRandomString()
        };
    }
}
