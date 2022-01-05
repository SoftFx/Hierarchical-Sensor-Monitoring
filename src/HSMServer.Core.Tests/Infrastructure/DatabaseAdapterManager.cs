using System;
using System.Collections.Generic;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Keys;
using HSMServer.Core.Model;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal sealed class DatabaseAdapterManager
    {
        private const string ProductName = "TestProduct";

        private static int _dbNumber;

        public DatabaseAdapter DatabaseAdapter { get; private set; }

        public Product TestProduct { get; }


        public DatabaseAdapterManager(string databaseFolder)
        {
            TestProduct = GetTestProduct();

            ++_dbNumber;

            DatabaseAdapter = new DatabaseAdapter(
                new DatabaseSettings()
                {
                    DatabaseFolder = databaseFolder,
                    EnvironmentDatabaseName = $"EnvironmentData{_dbNumber}",
                    MonitoringDatabaseName = $"MonitoringData{_dbNumber}",
                });
        }

        internal void AddTestProduct() =>
            DatabaseAdapter.AddProduct(TestProduct);

        internal void ClearDatabase()
        {
            DatabaseAdapter.Dispose();
            DatabaseAdapter = null;
        }

        private static Product GetTestProduct() =>
            new()
            {
                Name = ProductName,
                DateAdded = DateTime.UtcNow,
                Key = KeyGenerator.GenerateProductKey(ProductName),
                ExtraKeys = new List<ExtraProductKey>(),
            };
    }
}
