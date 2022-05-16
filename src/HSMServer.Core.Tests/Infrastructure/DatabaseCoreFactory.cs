using HSMCommon;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache.Entities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal static class DatabaseCoreFactory
    {
        internal static ProductEntity CreateProduct() => new()
        {
            Id = Guid.NewGuid().ToString(),
            AuthorId = Guid.NewGuid().ToString(),
            ParentProductId = Guid.NewGuid().ToString(),
            State = (int)ProductState.FullAccess,
            DisplayName = RandomGenerator.GetRandomString(),
            Description = RandomGenerator.GetRandomString(),
            CreationDate = DateTime.Now.Ticks,
            SubProductsIds = new List<string>() { Guid.NewGuid().ToString(), },
            SensorsIds = new List<string>(),
        };

        internal static User CreateUser() => new()
        {
            UserName = RandomGenerator.GetRandomString(),
            Password = HashComputer.ComputePasswordHash(RandomGenerator.GetRandomString()),
            IsAdmin = false
        };

        internal static RegistrationTicket CreateTicket() => new()
        {
            Role = nameof(ProductRoleEnum.ProductManager),
            ExpirationDate = DateTime.UtcNow.AddMinutes(30),
            ProductKey = Guid.NewGuid().ToString()
        };

        internal static ConfigurationObject CreateConfiguration(string name) => new()
        {
            Name = name,
            Value = RandomGenerator.GetRandomString(),
            Description = RandomGenerator.GetRandomString()
        };
    }
}
