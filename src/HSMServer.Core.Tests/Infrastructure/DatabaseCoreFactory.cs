using HSMCommon;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using System;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal static class DatabaseCoreFactory
    {
        internal static ProductEntity CreateProduct() =>
            EntitiesFactory.BuildProductEntity()
                           .AddSubProduct(Guid.NewGuid().ToString());

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
