using HSMServer.Core.Model;
using System;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal static class TestProductsManager
    {
        internal const string ProductName = "TestProduct";

        internal static Product TestProduct { get; } =
            new()
            {
                DisplayName = ProductName,
                CreationDate = DateTime.UtcNow,
                Id = Guid.NewGuid().ToString(),
            };
    }
}
