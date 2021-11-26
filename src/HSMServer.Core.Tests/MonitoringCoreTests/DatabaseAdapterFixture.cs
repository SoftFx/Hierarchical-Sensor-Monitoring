using System;
using System.Collections.Generic;
using System.IO;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Keys;
using HSMServer.Core.Model;

namespace HSMServer.Core.Tests
{
    public class DatabaseAdapterFixture : IDisposable
    {
        private const string DatabaseFolder = "TestDB";
        private const string ProductName = "TestProduct";

        public DatabaseAdapter DatabaseAdapter { get; }

        public Product TestProduct { get; }


        public DatabaseAdapterFixture()
        {
            TestProduct = GetTestProduct();

            DatabaseAdapter = new DatabaseAdapter(new DatabaseSettings() { DatabaseFolder = DatabaseFolder });
            DatabaseAdapter.AddProduct(TestProduct);

            SensorValuesTester.Initialize(this);
        }


        public void Dispose()
        {
            DatabaseAdapter.Dispose();
            Directory.Delete(DatabaseFolder, true);
        }

        private static Product GetTestProduct() =>
            new()
            {
                Name = ProductName,
                DateAdded = DateTime.Now,
                Key = KeyGenerator.GenerateProductKey(ProductName),
                ExtraKeys = new List<ExtraProductKey>(),
            };
    }
}
