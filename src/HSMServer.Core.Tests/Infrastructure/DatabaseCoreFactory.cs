using HSMCommon;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal static class DatabaseCoreFactory
    {
        internal static Product CreateProduct(string name) => new () 
        {
            DisplayName = name,
            CreationDate = DateTime.Now,
            Id = Guid.NewGuid().ToString()
        };

        internal static User CreateUser(string name) => new () 
        {
            UserName = name,
            Password = HashComputer.ComputePasswordHash(name),
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
