using System;
using System.Collections.Generic;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Keys;
using HSMServer.Core.Model;

namespace HSMServer.Core.Tests.MonitoringDataReceiverTests
{
    internal sealed class DatabaseAdapterManager
    {
        private const string ProductName = "TestProduct";
        internal const string DatabaseFolder = "TestDB";

        private static int _dbNumber;

        public DatabaseAdapter DatabaseAdapter { get; private set; }

        public Product TestProduct { get; }


        public DatabaseAdapterManager()
        {
            TestProduct = GetTestProduct();

            ++_dbNumber;

            DatabaseAdapter = new DatabaseAdapter(
                new DatabaseSettings()
                {
                    DatabaseFolder = DatabaseFolder,
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
