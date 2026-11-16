using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using System;

namespace HSMServer.Core.Tests.TreeValuesCacheTests.Fixture
{
    // Two root products in two folders, one key each (#1718).
    public sealed class SensorMoveFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(SensorMoveTests);

        internal Guid FolderA { get; private set; }
        internal Guid ProductA { get; } = Guid.NewGuid();
        internal Guid KeyA { get; private set; }

        internal Guid FolderB { get; private set; }
        internal Guid ProductB { get; } = Guid.NewGuid();
        internal Guid KeyB { get; private set; }

        internal const string ProductAName = "MoveProductA";
        internal const string ProductBName = "MoveProductB";


        internal override void InitializeDatabase(IDatabaseCore dbCore)
        {
            (FolderA, KeyA) = AddProduct(dbCore, ProductA, ProductAName);
            (FolderB, KeyB) = AddProduct(dbCore, ProductB, ProductBName);
        }

        private static (Guid Folder, Guid Key) AddProduct(IDatabaseCore dbCore, Guid productId, string name)
        {
            var folder = EntitiesFactory.BuildFolderEntity();
            dbCore.AddFolder(folder);

            dbCore.AddProduct(new ProductEntity
            {
                Id = productId.ToString(),
                DisplayName = name,
                CreationDate = DateTime.UtcNow.Ticks,
                State = 1 << 30,
                FolderId = folder.Id,
            });

            var key = EntitiesFactory.BuildAccessKeyEntity(productId: productId.ToString());
            dbCore.AddAccessKey(key);

            return (Guid.Parse(folder.Id), Guid.Parse(key.Id));
        }
    }
}
