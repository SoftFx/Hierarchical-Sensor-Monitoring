using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.AccessManager.DatabaseEntities.SnapshotEntity;
using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using HSMServer.BackgroundServices;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.NodeSettings;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Core.Tests.TreeValuesCacheTests.Fixture;
using HSMServer.Core.TreeStateSnapshot;
using HSMServer.Core.TreeStateSnapshot.States;
using HSMServer.Dashboards;
using Moq;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.TreeValuesCacheTests
{
    /// <summary>
    /// What reads a root product's NAME after the root is renamed. Ingestion does
    /// not (paths are root-relative, see <see cref="NodeRenameTests"/>), but the full
    /// path of every sensor of the product starts with it: alert templates and
    /// dashboard panel subscriptions match the full path, the by-name index is keyed
    /// by it, and the server's own collector finds its product by it.
    /// </summary>
    [Collection("Database collection")]
    public class RootRenameTests : MonitoringCoreTestsBase<RootRenameFixture>
    {
        public RootRenameTests(RootRenameFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture, addTestProduct: false) { }


        // Before the fix the sensors already in the product kept the templates of the
        // old name and missed those of the new one, while a sensor created after the
        // rename got the new name's templates: two sensors side by side under two sets
        // of alerts, and a restart changed nothing (template policies are stored on
        // the sensor).
        [Fact]
        [Trait("Category", "Root rename")]
        public async Task Rename_ReEvaluatesTheTemplates_OfTheSensorsAlreadyInTheProduct()
        {
            var oldName = UniqueRoot();
            var newName = UniqueRoot();
            var folder = Guid.NewGuid();

            var (product, key) = await AddProductAsync(oldName, folder);

            var oldNameTemplate = BuildTtlTemplate(folder, $"{oldName}/n/*");
            var newNameTemplate = BuildTtlTemplate(folder, $"{newName}/n/*");
            var anyNameTemplate = BuildTtlTemplate(folder, "*/n/*");

            // Templates are walked in id order: put the new name's first, so only the
            // detach-before-apply order keeps the two from overlapping on a sensor.
            if (newNameTemplate.Id.CompareTo(oldNameTemplate.Id) > 0)
                (newNameTemplate.Id, oldNameTemplate.Id) = (oldNameTemplate.Id, newNameTemplate.Id);

            foreach (var template in new[] { oldNameTemplate, newNameTemplate, anyNameTemplate })
                Assert.True((await _valuesCache.AddAlertTemplateAsync(template)).Success);

            var bothAttached = 0;
            void OnSensorChanged(BaseSensorModel changed, ActionType _)
            {
                // Raised on the queue thread right after each detach and each apply.
                if (HasTemplate(changed, oldNameTemplate) && HasTemplate(changed, newNameTemplate))
                    Interlocked.Increment(ref bothAttached);
            }

            try
            {
                var existing = await CreateIntSensorAsync(_valuesCache, product.Id, key, "n/existing");
                var deeper = await CreateIntSensorAsync(_valuesCache, product.Id, key, "n/deeper/s");

                Assert.True(HasTemplate(existing, oldNameTemplate), "test premise: the old name's template is attached");
                Assert.False(HasTemplate(existing, newNameTemplate));
                Assert.False(HasTemplate(deeper, oldNameTemplate), "test premise: `*` does not cross a segment");

                var ownTtl = TemplateFreeTtlIds(existing);
                var anyNamePolicy = existing.Policies.TTLPolicies.Single(t => t.TemplateId == anyNameTemplate.Id).Id;

                _valuesCache.ChangeSensorEvent += OnSensorChanged;

                var result = await _valuesCache.UpdateProductAsync(new ProductUpdate { Id = product.Id, Name = newName, Initiator = InitiatorInfo.AsUser("tester") }, default);

                _valuesCache.ChangeSensorEvent -= OnSensorChanged;

                Assert.True(result.IsOk, result.Error);
                Assert.Equal($"{newName}/n/existing", existing.FullPath);

                // The re-evaluation runs inside the rename, on the product's queue: it is over when the rename returns.
                Assert.False(HasTemplate(existing, oldNameTemplate), "the old name's template is detached");
                Assert.True(HasTemplate(existing, newNameTemplate), "the new name's template is applied");
                Assert.Equal(0, Volatile.Read(ref bothAttached));

                // A template that matches under both names, and the sensor's own alerts, are not touched.
                Assert.Equal(anyNamePolicy, existing.Policies.TTLPolicies.Single(t => t.TemplateId == anyNameTemplate.Id).Id);
                Assert.Equal(ownTtl, TemplateFreeTtlIds(existing));

                // A sensor no template named before or names now gets nothing.
                Assert.DoesNotContain(deeper.Policies.TTLPolicies, t => t.TemplateId is not null);

                // A sensor created after the rename agrees with the ones that were there.
                var created = await CreateIntSensorAsync(_valuesCache, product.Id, key, "n/created");
                Assert.False(HasTemplate(created, oldNameTemplate));
                Assert.True(HasTemplate(created, newNameTemplate));

                // And the store agrees with the memory.
                await using var restarted = BuildCacheOverTheSameDatabase();

                foreach (var path in new[] { "n/existing", "n/created" })
                {
                    Assert.True(restarted.TryGetSensorByPath(product.Id, path, out var reloaded));
                    Assert.False(HasTemplate(reloaded, oldNameTemplate), $"{path} after a restart: the old name's template");
                    Assert.True(HasTemplate(reloaded, newNameTemplate), $"{path} after a restart: the new name's template");
                    Assert.True(HasTemplate(reloaded, anyNameTemplate), $"{path} after a restart: the template of both names");
                }
            }
            finally
            {
                _valuesCache.ChangeSensorEvent -= OnSensorChanged;

                foreach (var template in new[] { oldNameTemplate, newNameTemplate, anyNameTemplate })
                    await _valuesCache.RemoveAlertTemplateAsync(template.Id);
            }
        }

        // The change-ownership rule of a node property: a name a user set is not
        // overwritten by a lower-priority initiator. Before the fix the by-name index
        // was re-keyed first: the product was listed under the name it did not get,
        // was not found by the name it kept, and the declined name stayed reserved.
        [Fact]
        [Trait("Category", "Root rename")]
        public async Task Rename_TheOwnershipCheckDeclines_LeavesTheByNameIndexOnTheKeptName()
        {
            var created = UniqueRoot();
            var userName = UniqueRoot();
            var declined = UniqueRoot();

            var product = await _valuesCache.AddProductAsync(created, Guid.Empty);

            var byUser = await _valuesCache.UpdateProductAsync(new ProductUpdate { Id = product.Id, Name = userName, Initiator = InitiatorInfo.AsUser("tester") }, default);
            Assert.True(byUser.IsOk, byUser.Error);
            Assert.Equal(userName, product.DisplayName);

            // The default initiator of an update is System, below User.
            var bySystem = await _valuesCache.UpdateProductAsync(new ProductUpdate { Id = product.Id, Name = declined, Description = "applied" }, default);

            Assert.True(bySystem.IsOk, bySystem.Error);
            Assert.Equal(userName, product.DisplayName);
            Assert.Equal("applied", product.Description);

            Assert.Same(product, _valuesCache.GetProductByName(userName));
            Assert.Null(_valuesCache.GetProductByName(declined));
            Assert.Single(_valuesCache.GetProducts(), p => p.Id == product.Id);

            // The declined name is free for another product.
            var other = await _valuesCache.AddProductAsync(declined, Guid.Empty);
            Assert.Same(other, _valuesCache.GetProductByName(declined));

            await using var restarted = BuildCacheOverTheSameDatabase();

            Assert.Equal(product.Id, restarted.GetProductByName(userName)?.Id);
            Assert.Equal(other.Id, restarted.GetProductByName(declined)?.Id);
        }

        // A forced update passes the ownership rule, and the index follows it.
        [Fact]
        [Trait("Category", "Root rename")]
        public async Task Rename_ByAForcedInitiator_AfterAUsersRename_IsApplied()
        {
            var userName = UniqueRoot();
            var forcedName = UniqueRoot();

            var product = await _valuesCache.AddProductAsync(UniqueRoot(), Guid.Empty);

            Assert.True((await _valuesCache.UpdateProductAsync(new ProductUpdate { Id = product.Id, Name = userName, Initiator = InitiatorInfo.AsUser("tester") }, default)).IsOk);
            Assert.True((await _valuesCache.UpdateProductAsync(new ProductUpdate { Id = product.Id, Name = forcedName, Initiator = InitiatorInfo.AsSystemForce("test") }, default)).IsOk);

            Assert.Equal(forcedName, product.DisplayName);
            Assert.Same(product, _valuesCache.GetProductByName(forcedName));
            Assert.Null(_valuesCache.GetProductByName(userName));
        }

        // The server's own collector finds its product by this name at every start
        // and creates it when the name is not there. Before the fix the rename went
        // through, and the next start created a second product: new sensors, the
        // history left behind under the renamed one.
        [Fact]
        [Trait("Category", "Root rename")]
        public async Task Rename_OfTheSelfMonitoringProduct_IsRefused_AndTheNextStartFindsTheSameProduct()
        {
            var key = DataCollectorWrapper.GetSelfMonitoringKeyAsync(_valuesCache);
            var product = _valuesCache.GetProductByName(DataCollectorWrapper.SelfMonitoringProductName);

            Assert.NotNull(product);
            var sensor = await CreateIntSensorAsync(_valuesCache, product.Id, key, "Database/size");

            var result = await _valuesCache.UpdateProductAsync(new ProductUpdate
            {
                Id = product.Id,
                Name = UniqueRoot(),
                Description = "renamed",
                Initiator = InitiatorInfo.AsUser("tester"),
            }, default);

            Assert.False(result.IsOk);
            Assert.Contains("self-monitoring", result.Error);
            Assert.Equal(DataCollectorWrapper.SelfMonitoringProductName, product.DisplayName);
            Assert.NotEqual("renamed", product.Description);

            // An update that keeps the name is applied as for any product.
            var kept = await _valuesCache.UpdateProductAsync(new ProductUpdate { Id = product.Id, Description = "kept" }, default);
            Assert.True(kept.IsOk, kept.Error);
            Assert.Equal("kept", product.Description);

            await using var restarted = BuildCacheOverTheSameDatabase();

            Assert.Equal(key, DataCollectorWrapper.GetSelfMonitoringKeyAsync(restarted));
            Assert.Equal(product.Id, restarted.GetProductByName(DataCollectorWrapper.SelfMonitoringProductName)?.Id);
            Assert.Single(restarted.GetProducts(), p => p.Id == product.Id);
            Assert.True(restarted.TryGetSensorByPath(product.Id, "Database/size", out var reloaded));
            Assert.Equal(sensor.Id, reloaded.Id);
        }

        // A panel subscription is a path template over the full path too. A sensor
        // it covers under the new name is added as a moved or a new one would be; a
        // source already on the panel stays (a panel does not record which
        // subscription added it).
        [Fact]
        [Trait("Category", "Root rename")]
        public async Task Rename_OffersTheSensorsOfTheProduct_ToTheDashboardSubscriptions()
        {
            var oldName = UniqueRoot();
            var newName = UniqueRoot();

            var (product, key) = await AddProductAsync(oldName, Guid.NewGuid());
            var covered = await CreateIntSensorAsync(_valuesCache, product.Id, key, "n/covered");
            var notCovered = await CreateIntSensorAsync(_valuesCache, product.Id, key, "other/s");

            using var dashboard = new Dashboard(new DashboardEntity
            {
                Id = Guid.NewGuid().ToByteArray(),
                Author = Guid.Empty.ToByteArray(),
                Name = "Root rename",
                Panels =
                [
                    BuildPanel($"{oldName}/n/*"),
                    BuildPanel($"{newName}/n/*"),
                ],
            }, _valuesCache);

            var panels = dashboard.Panels.Values.ToList();
            var oldNamePanel = panels.Single(p => p.Name == $"{oldName}/n/*");
            var newNamePanel = panels.Single(p => p.Name == $"{newName}/n/*");

            // A subscription loaded from the store adds what arrives, not what is there:
            // put the sensor on the old name's panel as its scan would have.
            oldNamePanel.AddSensor(covered);
            Assert.Single(oldNamePanel.Sources);
            Assert.Empty(newNamePanel.Sources);

            var result = await _valuesCache.UpdateProductAsync(new ProductUpdate { Id = product.Id, Name = newName, Initiator = InitiatorInfo.AsUser("tester") }, default);
            Assert.True(result.IsOk, result.Error);

            Assert.Equal(covered.Id, Assert.Single(newNamePanel.Sources).Value.Sensor.Id);
            Assert.Equal(covered.Id, Assert.Single(oldNamePanel.Sources).Value.Sensor.Id);
            Assert.DoesNotContain(panels.SelectMany(p => p.Sources.Values), s => s.Sensor.Id == notCovered.Id);

            // A second rename to the same panel's name adds nothing twice.
            Assert.True((await _valuesCache.UpdateProductAsync(new ProductUpdate { Id = product.Id, Name = oldName, Initiator = InitiatorInfo.AsUser("tester") }, default)).IsOk);
            Assert.Single(oldNamePanel.Sources);
            Assert.Single(newNamePanel.Sources);
        }


        private static string UniqueRoot() => $"root_{Guid.NewGuid():N}";

        private static bool HasTemplate(BaseSensorModel sensor, AlertTemplateModel template) =>
            sensor.Policies.TTLPolicies.Any(t => t.TemplateId == template.Id);

        private static System.Collections.Generic.List<Guid> TemplateFreeTtlIds(BaseSensorModel sensor) =>
            sensor.Policies.TTLPolicies.Where(t => t.TemplateId is null).Select(t => t.Id).OrderBy(id => id).ToList();

        private async Task<(ProductModel Product, Guid Key)> AddProductAsync(string name, Guid folder)
        {
            var product = await _valuesCache.AddProductAsync(name, Guid.Empty);

            // Templates and subscriptions are folder-scoped; to the cache a folder is its id.
            var result = await _valuesCache.UpdateProductAsync(new ProductUpdate { Id = product.Id, FolderId = folder }, default);
            Assert.True(result.IsOk, result.Error);

            var key = _valuesCache.AddAccessKey(new AccessKeyModel(EntitiesFactory.BuildAccessKeyEntity(productId: product.Id.ToString()))).Id;

            return (product, key);
        }

        private static async Task<BaseSensorModel> CreateIntSensorAsync(TreeValuesCache cache, Guid productId, Guid key, string path)
        {
            var value = SensorValuesFactory.BuildSensorValue(SensorType.Integer, path, DateTime.UtcNow.AddMinutes(-10));
            var result = await cache.AddSensorValueAsync(key, productId, value);

            Assert.True(result.IsOk, result.Error);
            Assert.True(cache.TryGetSensorByPath(productId, path, out var sensor), $"sensor {path} was not created");

            return sensor;
        }

        private static AlertTemplateModel BuildTtlTemplate(Guid folderId, string pathPattern)
        {
            var ttlSetting = new TimeIntervalSettingProperty();
            ttlSetting.TrySetValue(new TimeIntervalModel(TimeSpan.FromMinutes(5).Ticks));

            var template = new AlertTemplateModel
            {
                Name = $"Root rename template {Guid.NewGuid():N}",
                FolderId = folderId,
                SensorType = (byte)SensorType.Integer,
                Paths = [pathPattern],
                TtlEntries = [new TtlEntry(new TTLPolicy(ttlSetting, null), ttlSetting.Value ?? TimeIntervalModel.None)],
            };
            template.TryApplyPathTemplates(out _);

            return template;
        }

        private static DashboardPanelEntity BuildPanel(string pathTemplate) => new()
        {
            Id = Guid.NewGuid().ToByteArray(),
            Author = Guid.Empty.ToByteArray(),
            Name = pathTemplate,
            Subscriptions =
            [
                new PanelSubscriptionEntity
                {
                    Id = Guid.NewGuid().ToByteArray(),
                    PathTemplate = pathTemplate,
                    Color = "Red",
                    IsSubscribed = true,
                    IsApplied = true,
                },
            ],
        };

        private TreeValuesCache BuildCacheOverTheSameDatabase()
        {
            var snapshot = new Mock<ITreeStateSnapshot>();
            snapshot.Setup(a => a.Keys).Returns(new StateCollection<LastKeyState, LastKeyStateEntity>());

            var database = _databaseCoreManager.DatabaseCore;

            return new TreeValuesCache(database, snapshot.Object, _journalService, _alertScheduleProvider,
                new HSMServer.Core.ChangeSets.ChangeSetJournal(database));
        }
    }
}
