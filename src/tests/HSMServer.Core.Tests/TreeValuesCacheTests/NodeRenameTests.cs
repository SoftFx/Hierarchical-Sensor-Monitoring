using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities.SnapshotEntity;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.SensorsUpdatesQueue;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Core.Tests.TreeValuesCacheTests.Fixture;
using HSMServer.Core.TreeStateSnapshot;
using HSMServer.Core.TreeStateSnapshot.States;
using Moq;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.TreeValuesCacheTests
{
    /// <summary>
    /// A node's name and the ingestion index. The index maps a root-relative path to
    /// its sensor, and a node's name is a segment of the path of every sensor under
    /// it, so a node below the root is never renamed; a root's name is no part of a
    /// path and a root is renamed freely.
    /// </summary>
    [Collection("Database collection")]
    public class NodeRenameTests : MonitoringCoreTestsBase<NodeRenameFixture>
    {
        private const string RefusalText = "only a root product can be renamed";

        private readonly NodeRenameFixture _fixture;


        public NodeRenameTests(NodeRenameFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture, addTestProduct: false)
        {
            _fixture = fixture;
        }


        // Before the refusal the rename went through with the index left on the old
        // paths: `n/s` kept feeding the sensor now shown as `m/s`, a value at `m/s`
        // created a second sensor `s` in the same node, and after a restart (the index
        // is rebuilt from the tree) a value at `n/s` created a new node `n`.
        [Fact]
        [Trait("Category", "Node rename")]
        public async Task Rename_OfANodeBelowTheRoot_IsRefused_AndTheOldAndTheNewPathStayTwoPlainPaths()
        {
            var n = Unique();
            var m = Unique();

            var sensor = await CreateIntSensorAsync(_valuesCache, _fixture.Product, _fixture.Key, $"{n}/s");
            var node = sensor.Parent;

            var result = await _valuesCache.UpdateProductAsync(new ProductUpdate { Id = node.Id, Name = m, Description = "renamed" }, default);

            Assert.False(result.IsOk);
            Assert.Contains(RefusalText, result.Error);
            Assert.Contains($"{NodeRenameFixture.ProductName}/{n}", result.Error);

            // Nothing of the request is applied: the tree, the store and the index are as they were.
            Assert.Equal(n, node.DisplayName);
            Assert.NotEqual("renamed", node.Description);
            Assert.Equal($"{n}/s", sensor.Path);
            Assert.Equal($"{NodeRenameFixture.ProductName}/{n}/s", sensor.FullPath);
            Assert.Equal(n, _databaseCoreManager.DatabaseCore.GetProduct(node.Id.ToString()).DisplayName);
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.Product, $"{n}/s", out var indexed));
            Assert.Same(sensor, indexed);
            Assert.False(_valuesCache.TryGetSensorByPath(_fixture.Product, $"{m}/s", out _));

            // A collector that keeps sending the old path feeds the same sensor.
            var oldPathTime = DateTime.UtcNow.AddMinutes(-5);
            await AddIntAsync(_valuesCache, _fixture.Product, _fixture.Key, $"{n}/s", oldPathTime);
            Assert.Equal(oldPathTime, sensor.LastValue.Time);

            // A collector that sends the new path creates what any unknown path creates:
            // a node `m` of its own with its own sensor. The original is not touched.
            var newPathTime = DateTime.UtcNow.AddMinutes(-4);
            await AddIntAsync(_valuesCache, _fixture.Product, _fixture.Key, $"{m}/s", newPathTime);
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.Product, $"{m}/s", out var other));
            Assert.NotEqual(sensor.Id, other.Id);
            Assert.Equal(newPathTime, other.LastValue.Time);
            Assert.NotEqual(node.Id, other.Parent.Id);
            Assert.Equal(m, other.Parent.DisplayName);
            Assert.Equal(oldPathTime, sensor.LastValue.Time);
            Assert.Single(node.Sensors);
            Assert.Single(other.Parent.Sensors);

            // The restart rebuilds the index from the stored tree: the same two sensors
            // at the same two paths, and each path keeps feeding its own sensor.
            await using var restarted = BuildCacheOverTheSameDatabase();

            Assert.True(restarted.TryGetSensorByPath(_fixture.Product, $"{n}/s", out var reloaded));
            Assert.Equal(sensor.Id, reloaded.Id);
            Assert.Equal(n, reloaded.Parent.DisplayName);
            Assert.True(restarted.TryGetSensorByPath(_fixture.Product, $"{m}/s", out var reloadedOther));
            Assert.Equal(other.Id, reloadedOther.Id);

            var afterRestart = DateTime.UtcNow.AddMinutes(-3);
            await AddIntAsync(restarted, _fixture.Product, _fixture.Key, $"{n}/s", afterRestart);
            await AddIntAsync(restarted, _fixture.Product, _fixture.Key, $"{m}/s", afterRestart.AddSeconds(1));

            Assert.Equal(afterRestart, reloaded.LastValue.Time);
            Assert.Equal(afterRestart.AddSeconds(1), reloadedOther.LastValue.Time);
            Assert.Single(reloaded.Parent.Sensors);
            Assert.Single(reloadedOther.Parent.Sensors);
            Assert.Single(restarted.GetProduct(_fixture.Product).SubProducts.Values, p => p.DisplayName == n);
            Assert.Single(restarted.GetProduct(_fixture.Product).SubProducts.Values, p => p.DisplayName == m);
        }

        [Fact]
        [Trait("Category", "Node rename")]
        public async Task Rename_OfANestedNode_AndOfItsParent_IsRefused()
        {
            var top = Unique();
            var inner = Unique();

            var sensor = await CreateIntSensorAsync(_valuesCache, _fixture.Product, _fixture.Key, $"{top}/{inner}/s");
            var innerNode = sensor.Parent;
            var topNode = innerNode.Parent;

            foreach (var node in new[] { innerNode, topNode })
            {
                var result = await _valuesCache.UpdateProductAsync(new ProductUpdate { Id = node.Id, Name = Unique() }, default);

                Assert.False(result.IsOk);
                Assert.Contains(RefusalText, result.Error);
            }

            Assert.Equal($"{top}/{inner}/s", sensor.Path);
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.Product, $"{top}/{inner}/s", out var indexed));
            Assert.Same(sensor, indexed);
        }

        // The Edit Product form posts the name only when it changed; other callers
        // send none. Neither is a rename, and the rest of the update is applied.
        [Theory]
        [Trait("Category", "Node rename")]
        [InlineData(true)]
        [InlineData(false)]
        public async Task Update_OfANode_ThatKeepsItsName_IsApplied(bool sendsTheCurrentName)
        {
            var n = Unique();

            var sensor = await CreateIntSensorAsync(_valuesCache, _fixture.Product, _fixture.Key, $"{n}/s");
            var node = sensor.Parent;

            var result = await _valuesCache.UpdateProductAsync(new ProductUpdate
            {
                Id = node.Id,
                Name = sendsTheCurrentName ? n : null,
                Description = "kept its name",
            }, default);

            Assert.True(result.IsOk, result.Error);
            Assert.Equal("kept its name", node.Description);
            Assert.Equal(n, node.DisplayName);
        }

        // The handler that applies the update holds the same rule: a request that
        // reaches a product queue some other way than UpdateProductAsync renames nothing.
        [Fact]
        [Trait("Category", "Node rename")]
        public async Task Rename_OfANode_IsRefusedByTheQueueHandlerToo()
        {
            var n = Unique();

            var sensor = await CreateIntSensorAsync(_valuesCache, _fixture.Product, _fixture.Key, $"{n}/s");
            var node = sensor.Parent;

            var queue = new Mock<IUpdatesQueue>();
            queue.Setup(q => q.Stopwatch).Returns(new Stopwatch());

            var error = Assert.Throws<InvalidOperationException>(() =>
                ((IUpdateHandler)_valuesCache).ProcessRequest(queue.Object, new ProductUpdate { Id = node.Id, Name = Unique() }));

            Assert.Contains(RefusalText, error.Message);
            Assert.Equal(n, node.DisplayName);
            Assert.Equal(n, _databaseCoreManager.DatabaseCore.GetProduct(node.Id.ToString()).DisplayName);
        }

        // The neighbour: a root's name is no part of a sensor path (paths are
        // root-relative and the index is keyed by the root's id), so renaming a root
        // moves no sensor.
        [Fact]
        [Trait("Category", "Node rename")]
        public async Task Rename_OfARootProduct_KeepsEverySensorPath_AlsoAfterARestart()
        {
            var oldName = $"root_{Guid.NewGuid():N}";
            var newName = $"root_{Guid.NewGuid():N}";
            var n = Unique();

            var product = await _valuesCache.AddProductAsync(oldName, Guid.Empty);
            var key = AddKey(product.Id);
            var sensor = await CreateIntSensorAsync(_valuesCache, product.Id, key, $"{n}/s");

            var result = await _valuesCache.UpdateProductAsync(new ProductUpdate { Id = product.Id, Name = newName }, default);

            Assert.True(result.IsOk, result.Error);
            Assert.Equal(newName, product.DisplayName);
            Assert.Same(product, _valuesCache.GetProductByName(newName));
            Assert.Null(_valuesCache.GetProductByName(oldName));

            Assert.Equal($"{n}/s", sensor.Path);
            Assert.Equal($"{newName}/{n}/s", sensor.FullPath);
            Assert.True(_valuesCache.TryGetSensorByPath(product.Id, $"{n}/s", out var indexed));
            Assert.Same(sensor, indexed);

            var time = DateTime.UtcNow.AddMinutes(-5);
            await AddIntAsync(_valuesCache, product.Id, key, $"{n}/s", time);
            Assert.Equal(time, sensor.LastValue.Time);
            Assert.Single(sensor.Parent.Sensors);

            await using var restarted = BuildCacheOverTheSameDatabase();

            Assert.Equal(product.Id, restarted.GetProductByName(newName)?.Id);
            Assert.Null(restarted.GetProductByName(oldName));
            Assert.True(restarted.TryGetSensorByPath(product.Id, $"{n}/s", out var reloaded));
            Assert.Equal(sensor.Id, reloaded.Id);

            var afterRestart = DateTime.UtcNow.AddMinutes(-3);
            await AddIntAsync(restarted, product.Id, key, $"{n}/s", afterRestart);
            Assert.Equal(afterRestart, reloaded.LastValue.Time);
            Assert.Single(reloaded.Parent.Sensors);
        }

        // An empty name used to be written to the root while the by-name index kept
        // the old name.
        [Theory]
        [Trait("Category", "Node rename")]
        [InlineData("")]
        [InlineData("   ")]
        public async Task Rename_OfARootProduct_ToAnEmptyName_IsRefused(string emptyName)
        {
            var name = $"root_{Guid.NewGuid():N}";
            var product = await _valuesCache.AddProductAsync(name, Guid.Empty);

            var result = await _valuesCache.UpdateProductAsync(new ProductUpdate { Id = product.Id, Name = emptyName }, default);

            Assert.False(result.IsOk);
            Assert.Contains("the name is empty", result.Error);
            Assert.Equal(name, product.DisplayName);
            Assert.Same(product, _valuesCache.GetProductByName(name));
            Assert.Equal(name, _databaseCoreManager.DatabaseCore.GetProduct(product.Id.ToString()).DisplayName);
        }


        private static string Unique() => $"n{Guid.NewGuid():N}"[..12];

        private static async Task<BaseSensorModel> CreateIntSensorAsync(TreeValuesCache cache, Guid productId, Guid key, string path)
        {
            await AddIntAsync(cache, productId, key, path, DateTime.UtcNow.AddMinutes(-10));

            Assert.True(cache.TryGetSensorByPath(productId, path, out var sensor), $"sensor {path} was not created");

            return sensor;
        }

        // AddSensorValueAsync is awaited through the product's queue: the value is
        // applied when it returns.
        private static async Task AddIntAsync(TreeValuesCache cache, Guid productId, Guid key, string path, DateTime time)
        {
            var value = SensorValuesFactory.BuildSensorValue(SensorType.Integer, path, time);
            var result = await cache.AddSensorValueAsync(key, productId, value);

            Assert.True(result.IsOk, result.Error);
        }

        private Guid AddKey(Guid productId) =>
            _valuesCache.AddAccessKey(new AccessKeyModel(EntitiesFactory.BuildAccessKeyEntity(productId: productId.ToString()))).Id;

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
