using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using System;

namespace HSMServer.Core.Tests.TreeValuesCacheTests.Fixture
{
    public sealed class TemplateApplicationFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(TemplateApplicationTests);

        internal Guid FolderId { get; private set; }
        internal Guid ProductId { get; } = Guid.NewGuid();
        internal Guid AccessKeyId { get; private set; }

        internal Guid FolderId2 { get; private set; }
        internal Guid ProductId2 { get; } = Guid.NewGuid();
        internal Guid AccessKeyId2 { get; private set; }


        internal override void InitializeDatabase(IDatabaseCore dbCore)
        {
            var folder = EntitiesFactory.BuildFolderEntity();
            FolderId = Guid.Parse(folder.Id);
            dbCore.AddFolder(folder);

            var product = new ProductEntity
            {
                Id = ProductId.ToString(),
                DisplayName = "TemplateTestProduct",
                CreationDate = DateTime.UtcNow.Ticks,
                State = 1 << 30,
                FolderId = folder.Id,
            };
            dbCore.AddProduct(product);

            var key = EntitiesFactory.BuildAccessKeyEntity(productId: product.Id);
            AccessKeyId = Guid.Parse(key.Id);
            dbCore.AddAccessKey(key);

            // Second folder + product for tests that move a template between folders (#1209).
            var folder2 = EntitiesFactory.BuildFolderEntity();
            FolderId2 = Guid.Parse(folder2.Id);
            dbCore.AddFolder(folder2);

            var product2 = new ProductEntity
            {
                Id = ProductId2.ToString(),
                DisplayName = "TemplateTestProduct2",
                CreationDate = DateTime.UtcNow.Ticks,
                State = 1 << 30,
                FolderId = folder2.Id,
            };
            dbCore.AddProduct(product2);

            var key2 = EntitiesFactory.BuildAccessKeyEntity(productId: product2.Id);
            AccessKeyId2 = Guid.Parse(key2.Id);
            dbCore.AddAccessKey(key2);
        }
    }
}
