using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using System;

namespace HSMServer.Core.Tests.TreeValuesCacheTests.Fixture
{
    // One root product with one key.
    public sealed class NodeRenameFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(NodeRenameTests);

        internal Guid Product { get; } = Guid.NewGuid();

        internal Guid Key { get; private set; }

        internal const string ProductName = "NodeRenameProduct";


        internal override void InitializeDatabase(IDatabaseCore dbCore)
        {
            dbCore.AddProduct(new ProductEntity
            {
                Id = Product.ToString(),
                DisplayName = ProductName,
                CreationDate = DateTime.UtcNow.Ticks,
                State = 1 << 30,
            });

            var key = EntitiesFactory.BuildAccessKeyEntity(productId: Product.ToString());
            dbCore.AddAccessKey(key);

            Key = Guid.Parse(key.Id);
        }
    }
}
