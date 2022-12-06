using HSMCommon.Constants;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model;
using System;

namespace HSMServer.Core.Tests.Infrastructure
{
    internal static class TestProductsManager
    {
        internal const string ProductName = "TestProduct";

        internal static readonly Guid ProductId = Guid.NewGuid();
        internal static readonly Guid ProductKeyId = Guid.NewGuid();


        internal static ProductEntity TestProduct { get; } =
            new()
            {
                Id = ProductId.ToString(),
                DisplayName = ProductName,
                CreationDate = DateTime.UtcNow.Ticks,
                State = 1 << 30,
            };

        internal static AccessKeyEntity TestProductKey { get; } =
            EntitiesFactory.BuildAccessKeyEntity(id: ProductKeyId.ToString(),
                                                 name: CommonConstants.DefaultAccessKey,
                                                 productId: TestProduct.Id);
    }
}
