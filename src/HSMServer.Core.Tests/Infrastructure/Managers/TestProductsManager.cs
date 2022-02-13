using HSMServer.Core.Keys;
using HSMServer.Core.Model;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.Tests.Infrastructure
{
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
}
