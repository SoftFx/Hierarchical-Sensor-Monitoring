using System;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;

namespace HSMServer.Core.Tests.TreeValuesCacheTests.Fixture
{
    public sealed class TemplateConcurrencyFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(TemplateConcurrencyTests);

        internal Guid FolderId { get; private set; }

        internal Guid ProductAId { get; } = Guid.NewGuid();
        internal Guid ProductBId { get; } = Guid.NewGuid();
        internal Guid SubProductAId { get; } = Guid.NewGuid();

        internal Guid AccessKeyAId { get; private set; }
        internal Guid AccessKeyBId { get; private set; }


        internal override void InitializeDatabase(IDatabaseCore dbCore)
        {
            var folder = EntitiesFactory.BuildFolderEntity();
            FolderId = Guid.Parse(folder.Id);
            dbCore.AddFolder(folder);

            dbCore.AddProduct(BuildProduct(ProductAId, "ProductA_concurrency", folder.Id));
            dbCore.AddProduct(BuildProduct(ProductBId, "ProductB_concurrency", folder.Id));
            dbCore.AddProduct(BuildSubProduct(SubProductAId, ProductAId, "SubProductA_concurrency"));

            AccessKeyAId = AddKey(dbCore, ProductAId);
            AccessKeyBId = AddKey(dbCore, ProductBId);
        }

        private static ProductEntity BuildProduct(Guid id, string name, string folderId) =>
            new()
            {
                Id = id.ToString(),
                DisplayName = name,
                CreationDate = DateTime.UtcNow.Ticks,
                State = 1 << 30,
                FolderId = folderId,
            };

        private static ProductEntity BuildSubProduct(Guid id, Guid parentId, string name) =>
            new()
            {
                Id = id.ToString(),
                ParentProductId = parentId.ToString(),
                DisplayName = name,
                CreationDate = DateTime.UtcNow.Ticks,
                State = 1 << 30,
            };

        private static Guid AddKey(IDatabaseCore dbCore, Guid productId)
        {
            var key = EntitiesFactory.BuildAccessKeyEntity(productId: productId.ToString());
            dbCore.AddAccessKey(key);
            return Guid.Parse(key.Id);
        }
    }
}
