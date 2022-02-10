using HSMCommon;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Keys;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal class DatabaseAdapterManager
    {
        private static int _dbNumber;


        public string DatabaseFolder { get; }

        public DatabaseAdapter DatabaseAdapter { get; private set; }


        public DatabaseAdapterManager(string databaseFolder)
        {
            ++_dbNumber;

            DatabaseFolder = databaseFolder;
            DatabaseAdapter = new DatabaseAdapter(
                new DatabaseSettings()
                {
                    DatabaseFolder = databaseFolder,
                    EnvironmentDatabaseName = $"EnvironmentData{_dbNumber}",
                    MonitoringDatabaseName = $"MonitoringData{_dbNumber}",
                });
        }


        internal void ClearDatabase()
        {
            DatabaseAdapter.Dispose();
            DatabaseAdapter = null;
        }

        internal void AddTestProduct() =>
            DatabaseAdapter.AddProduct(TestProductsManager.TestProduct);
    }


    internal static class TestProductsManager
    {
        internal const string ProductName = "TestProduct";

        internal static Product TestProduct { get; } =
            new()
            {
                Name = ProductName,
                DateAdded = DateTime.UtcNow,
                Key = KeyGenerator.GenerateProductKey(ProductName),
                ExtraKeys = new List<ExtraProductKey>(),
            };
    }


    internal static class TestUsersManager
    {
        private const string DefaultUserName = "default";
        private const string DefaultUserCertificateFileName = "default.client.crt";
        private const string DefaultUserCertificateThumbprint = "a563183e1fec784f45bc8f3aa47c40eba1a26df9";

        private const string TestUserName = "TestUserName";

        internal static User DefaultUser { get; } =
            new(DefaultUserName)
            {
                CertificateFileName = DefaultUserCertificateFileName,
                CertificateThumbprint = DefaultUserCertificateThumbprint,
                IsAdmin = true,
                Password = HashComputer.ComputePasswordHash(DefaultUserName),
            };

        internal static User TestUser { get; } =
            new(TestUserName)
            {
                CertificateFileName = RandomGenerator.GetRandomString(),
                CertificateThumbprint = RandomGenerator.GetRandomString(40),
                IsAdmin = RandomGenerator.GetRandomBool(),
                Password = HashComputer.ComputePasswordHash(TestUserName),
                ProductsRoles = new List<KeyValuePair<string, ProductRoleEnum>>()
                {
                    new KeyValuePair<string, ProductRoleEnum>(TestProductsManager.ProductName, (ProductRoleEnum)RandomGenerator.GetRandomInt(min: 0, max: 2))
                },
            };
    }
}
