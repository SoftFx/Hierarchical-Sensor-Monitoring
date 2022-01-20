using HSMServer.Core.Keys;
using HSMServer.Core.Model;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal sealed class DatabaseAdapterProductsManager : DatabaseAdapterManager
    {
        internal const string ProductName = "TestProduct";

        public Product TestProduct { get; }


        internal DatabaseAdapterProductsManager(string dbFolder) : base(dbFolder) =>
            TestProduct = GetTestProduct();


        internal void AddTestProduct() =>
            DatabaseAdapter.AddProduct(TestProduct);

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
