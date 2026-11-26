using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Model.Folders;
using Xunit;
using CoreTimeInterval = HSMServer.Core.Model.TimeInterval;

namespace HSMServer.Core.Tests.Folders
{
    public sealed class DefaultRetentionFixture : DatabaseFixture
    {
        internal Guid StoredProductId { get; } = Guid.NewGuid();

        protected override string DatabaseFolder => nameof(DefaultRetentionTests);


        // A product created before the default changed: its month is stored with it.
        internal override void InitializeDatabase(HSMServer.Core.DataLayer.IDatabaseCore dbCore) =>
            dbCore.AddProduct(new ProductEntity
            {
                Id = StoredProductId.ToString(),
                DisplayName = "stored-before-the-change",
                CreationDate = DateTime.UtcNow.Ticks,
                State = (int)ProductState.FullAccess,
                Settings = new Dictionary<string, TimeIntervalEntity>
                {
                    ["KeepHistory"] = new((long)CoreTimeInterval.Month, 0L),
                    ["SelfDestroy"] = new((long)CoreTimeInterval.Month, 0L),
                },
            });
    }


    // "Keep sensor history" and "Remove sensor after inactivity" of a new root
    // product and of a new folder are three months (owner's decision,
    // 2026-11-25; one month before). Only new ones: what is stored keeps its
    // period.
    [Collection("Database collection")]
    public class DefaultRetentionTests : MonitoringCoreTestsBase<DefaultRetentionFixture>
    {
        private readonly DefaultRetentionFixture _fixture;


        public DefaultRetentionTests(DefaultRetentionFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture, addTestProduct: false)
        {
            _fixture = fixture;
        }


        [Fact]
        public async Task NewProduct_KeepsHistoryAndRemovesIdleSensorsAfterThreeMonths()
        {
            var product = await _valuesCache.AddProductAsync($"product-{Guid.NewGuid():N}", Guid.NewGuid());

            Assert.Equal(CoreTimeInterval.ThreeMonths, product.Settings.KeepHistory.CurValue.Interval);
            Assert.Equal(CoreTimeInterval.ThreeMonths, product.Settings.SelfDestroy.CurValue.Interval);
            Assert.True(product.Settings.TTL.CurValue.IsNone);

            var stored = _databaseCoreManager.GetProduct(product.Id).Settings;

            Assert.Equal(new TimeIntervalEntity((long)CoreTimeInterval.ThreeMonths, 0L), stored["KeepHistory"]);
            Assert.Equal(new TimeIntervalEntity((long)CoreTimeInterval.ThreeMonths, 0L), stored["SelfDestroy"]);
        }

        [Fact]
        public void StoredProduct_KeepsItsMonth()
        {
            var product = _valuesCache.GetProduct(_fixture.StoredProductId);

            Assert.Equal(CoreTimeInterval.Month, product.Settings.KeepHistory.CurValue.Interval);
            Assert.Equal(CoreTimeInterval.Month, product.Settings.SelfDestroy.CurValue.Interval);
        }

        [Fact]
        public void NewFolder_KeepsHistoryAndRemovesIdleSensorsAfterThreeMonths()
        {
            var folder = new FolderModel(new FolderAdd
            {
                Name = "new-folder",
                Products = [],
                AuthorId = Guid.NewGuid(),
                Initiator = InitiatorInfo.System,
            });

            Assert.Equal(CoreTimeInterval.ThreeMonths, folder.KeepHistory.ToModel().Interval);
            Assert.Equal(CoreTimeInterval.ThreeMonths, folder.SelfDestroy.ToModel().Interval);
            Assert.True(folder.TTL.ToModel().IsNone);

            // What the server stores for it.
            var entity = folder.ToEntity();

            Assert.Equal(new TimeIntervalEntity((long)CoreTimeInterval.ThreeMonths, 0L), entity.Settings["KeepHistory"]);
            Assert.Equal(new TimeIntervalEntity((long)CoreTimeInterval.ThreeMonths, 0L), entity.Settings["SelfDestroy"]);
        }

        [Fact]
        public void StoredFolderWithoutPeriods_StillReadsTheMonth()
        {
            // A folder entity that carries no periods read as a month before
            // the change; it must not start keeping more on an upgrade.
            var folder = new FolderModel(new FolderEntity
            {
                Id = Guid.NewGuid().ToString(),
                AuthorId = Guid.NewGuid().ToString(),
                CreationDate = DateTime.UtcNow.Ticks,
                DisplayName = "stored-folder",
            });

            Assert.Equal(CoreTimeInterval.Month, folder.KeepHistory.ToModel().Interval);
            Assert.Equal(CoreTimeInterval.Month, folder.SelfDestroy.ToModel().Interval);
        }

        [Fact]
        public void StoredFolderWithPeriods_KeepsThem()
        {
            var folder = new FolderModel(new FolderEntity
            {
                Id = Guid.NewGuid().ToString(),
                AuthorId = Guid.NewGuid().ToString(),
                CreationDate = DateTime.UtcNow.Ticks,
                DisplayName = "stored-folder-with-periods",
                Settings = new Dictionary<string, TimeIntervalEntity>
                {
                    ["KeepHistory"] = new((long)CoreTimeInterval.Year, 0L),
                    ["SelfDestroy"] = new((long)CoreTimeInterval.Month, 0L),
                },
            });

            Assert.Equal(CoreTimeInterval.Year, folder.KeepHistory.ToModel().Interval);
            Assert.Equal(CoreTimeInterval.Month, folder.SelfDestroy.ToModel().Interval);
        }
    }
}
