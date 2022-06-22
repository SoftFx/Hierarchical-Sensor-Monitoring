using HSMDatabase.AccessManager.DatabaseEntities;
using System;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal static class TestProductsManager
    {
        internal const string ProductName = "TestProduct";

        internal static ProductEntity TestProduct { get; } =
            new()
            {
                Id = Guid.NewGuid().ToString(),
                DisplayName = ProductName,
                CreationDate = DateTime.UtcNow.Ticks,
            };
    }
}
