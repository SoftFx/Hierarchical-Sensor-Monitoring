using HSMCommon;
using HSMServer.Core.Keys;
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
            Name = name,
            DateAdded = DateTime.Now,
            Key = KeyGenerator.GenerateProductKey(name),
            ExtraKeys = new List<ExtraProductKey>()
        };

        internal static ExtraProductKey CreateExtraKey(string productName, string extraKeyName) => new()
        {
            Key = KeyGenerator.GenerateExtraProductKey(productName, extraKeyName),
            Name = extraKeyName
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
            ProductKey = KeyGenerator.GenerateProductKey(RandomGenerator.GetRandomString())
        };

        internal static ConfigurationObject CreateConfiguration(string name) => new()
        {
            Name = name,
            Value = RandomGenerator.GetRandomString()
        };
    }
}
