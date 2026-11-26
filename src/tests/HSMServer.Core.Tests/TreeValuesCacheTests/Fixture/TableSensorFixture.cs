using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using System;

namespace HSMServer.Core.Tests.TreeValuesCacheTests.Fixture
{
    // One root product with one key (#1844).
    public sealed class TableSensorFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(TableSensorTests);

        internal Guid Folder { get; private set; }
        internal Guid Product { get; } = Guid.NewGuid();
        internal Guid Key { get; private set; }

        internal const string ProductName = "TableProduct";


        internal override void InitializeDatabase(IDatabaseCore dbCore)
        {
            var folder = EntitiesFactory.BuildFolderEntity();
            dbCore.AddFolder(folder);

            dbCore.AddProduct(new ProductEntity
            {
                Id = Product.ToString(),
                DisplayName = ProductName,
                CreationDate = DateTime.UtcNow.Ticks,
                State = 1 << 30,
                FolderId = folder.Id,
            });

            var key = EntitiesFactory.BuildAccessKeyEntity(productId: Product.ToString());
            dbCore.AddAccessKey(key);

            Folder = Guid.Parse(folder.Id);
            Key = Guid.Parse(key.Id);
        }
    }
}
