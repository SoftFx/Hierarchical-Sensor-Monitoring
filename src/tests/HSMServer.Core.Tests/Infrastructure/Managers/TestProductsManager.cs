using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model;
using System;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal static class TestProductsManager
    {
        internal const string ProductName = "TestProduct";

        internal static Guid ProductId { get; } = Guid.NewGuid();


        internal static ProductEntity TestProduct { get; } =
            new()
            {
                Id = ProductId.ToString(),
                DisplayName = ProductName,
                CreationDate = DateTime.UtcNow.Ticks,
                State = 1 << 30,
            };


        internal static ProductModel TestProductModel = new ProductModel(TestProduct);

        internal static AccessKeyEntity TestProductKey { get; } =
            EntitiesFactory.BuildAccessKeyEntity(productId: TestProduct.Id);
    }
}
