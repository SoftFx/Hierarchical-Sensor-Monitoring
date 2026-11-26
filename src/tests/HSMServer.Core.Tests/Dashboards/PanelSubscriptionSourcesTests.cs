using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities.VisualEntity;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Dashboards;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.Dashboards
{
    public sealed class PanelSubscriptionSourcesFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(PanelSubscriptionSourcesTests);
    }


    /// <summary>
    /// The sources a panel subscription adds by itself: for a sensor that is
    /// created, and for one that is moved, under a path its template covers.
    /// The dashboards are built by a real <see cref="DashboardManager"/> over the
    /// real cache and store, so "stored" means read back by a second manager.
    /// </summary>
    public class PanelSubscriptionSourcesTests : MonitoringCoreTestsBase<PanelSubscriptionSourcesFixture>
    {
        public PanelSubscriptionSourcesTests(PanelSubscriptionSourcesFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture, addTestProduct: false) { }


        // Before the fix the source was added to the panel in memory only: nothing
        // told the manager to store the dashboard, and after a restart the sensor was
        // no longer new, so the subscription never added it again.
        [Fact]
        [Trait("Category", "Panel subscriptions")]
        public async Task SourceAddedForANewSensor_IsStored()
        {
            var (product, key) = await AddProductAsync(Guid.NewGuid());

            var dashboards = new DashboardManager(_databaseCoreManager.DatabaseCore, _valuesCache);

            try
            {
                var (dashboard, panel) = await AddDashboardAsync(dashboards, BuildPanel($"{product.DisplayName}/n/*"));

                var stored = 0;
                dashboard.UpdatedEvent += () => Interlocked.Increment(ref stored);

                var sensor = await CreateSensorAsync(product.Id, key, "n/cpu");

                Assert.Equal(sensor.Id, Assert.Single(panel.Sources).Value.SensorId);
                Assert.Equal(new[] { sensor.Id }, ReloadSourceSensors(dashboard.Id, panel.Id));
                Assert.Equal(1, Volatile.Read(ref stored));

                // A sensor no subscription covers costs the dashboard no write.
                await CreateSensorAsync(product.Id, key, "other/cpu");

                Assert.Single(panel.Sources);
                Assert.Equal(1, Volatile.Read(ref stored));
            }
            finally
            {
                dashboards.Dispose();
            }
        }

        [Fact]
        [Trait("Category", "Panel subscriptions")]
        public async Task SourceAddedForAMovedSensor_IsStored()
        {
            var (product, key) = await AddProductAsync(Guid.NewGuid());

            var dashboards = new DashboardManager(_databaseCoreManager.DatabaseCore, _valuesCache);

            try
            {
                var (dashboard, panel) = await AddDashboardAsync(dashboards, BuildPanel($"{product.DisplayName}/n/*"));

                var sensor = await CreateSensorAsync(product.Id, key, "other/cpu");
                Assert.Empty(panel.Sources);

                var stored = 0;
                dashboard.UpdatedEvent += () => Interlocked.Increment(ref stored);

                Assert.True((await _valuesCache.MoveSensorAsync(sensor.Id, product.Id, "n/cpu")).IsOk);

                Assert.Equal(sensor.Id, Assert.Single(panel.Sources).Value.SensorId);
                Assert.Equal(new[] { sensor.Id }, ReloadSourceSensors(dashboard.Id, panel.Id));
                Assert.Equal(1, Volatile.Read(ref stored));

                // Already on the panel: a second move under the template adds and stores nothing.
                Assert.True((await _valuesCache.MoveSensorAsync(sensor.Id, product.Id, "n/cpu2")).IsOk);

                Assert.Single(panel.Sources);
                Assert.Equal(1, Volatile.Read(ref stored));
            }
            finally
            {
                dashboards.Dispose();
            }
        }

        // Two subscriptions of one panel that cover the same sensor add a source
        // each; the dashboard is written once for the sensor, not once per source.
        [Fact]
        [Trait("Category", "Panel subscriptions")]
        public async Task SourcesAddedByTwoSubscriptionsOfAPanel_AreStoredWithOneWrite()
        {
            var (product, key) = await AddProductAsync(Guid.NewGuid());

            var dashboards = new DashboardManager(_databaseCoreManager.DatabaseCore, _valuesCache);

            try
            {
                var (dashboard, panel) = await AddDashboardAsync(dashboards,
                    BuildPanel($"{product.DisplayName}/n/*", $"{product.DisplayName}/*/cpu"));

                var stored = 0;
                dashboard.UpdatedEvent += () => Interlocked.Increment(ref stored);

                var sensor = await CreateSensorAsync(product.Id, key, "n/cpu");

                Assert.Equal(2, panel.Sources.Count);
                Assert.Equal(new[] { sensor.Id, sensor.Id }, ReloadSourceSensors(dashboard.Id, panel.Id));
                Assert.Equal(1, Volatile.Read(ref stored));
            }
            finally
            {
                dashboards.Dispose();
            }
        }

        // Before the fix the folder filter read the value of a folder the product
        // does not have. The exception left the dashboard's handler of the "sensor
        // added" event, and the cache rolled the new sensor back.
        [Fact]
        [Trait("Category", "Panel subscriptions")]
        public async Task SensorOfAFolderlessProduct_UnderAFolderRestrictedSubscription_IsCreated_AndNotAdded()
        {
            var (product, key) = await AddProductAsync(folder: null);

            var dashboards = new DashboardManager(_databaseCoreManager.DatabaseCore, _valuesCache);

            try
            {
                var (_, panel) = await AddDashboardAsync(dashboards,
                    BuildPanel(folders: [Guid.NewGuid()], property: PlottedProperty.Value, $"{product.DisplayName}/n/*"));

                var value = SensorValuesFactory.BuildSensorValue(SensorType.Integer, "n/cpu", DateTime.UtcNow.AddMinutes(-1));
                var result = await _valuesCache.AddSensorValueAsync(key, product.Id, value);

                Assert.True(result.IsOk, result.Error);
                Assert.True(_valuesCache.TryGetSensorByPath(product.Id, "n/cpu", out _), "the sensor was not created");
                Assert.Empty(panel.Sources);
            }
            finally
            {
                dashboards.Dispose();
            }
        }

        // The same filter with "no folder" selected covers the folderless product.
        [Fact]
        [Trait("Category", "Panel subscriptions")]
        public async Task SensorOfAFolderlessProduct_UnderASubscriptionThatSelectsNoFolder_IsAdded()
        {
            var (product, key) = await AddProductAsync(folder: null);

            var dashboards = new DashboardManager(_databaseCoreManager.DatabaseCore, _valuesCache);

            try
            {
                var (_, panel) = await AddDashboardAsync(dashboards,
                    BuildPanel(folders: [Guid.NewGuid(), Guid.Empty], property: PlottedProperty.Value, $"{product.DisplayName}/n/*"));

                var sensor = await CreateSensorAsync(product.Id, key, "n/cpu");

                Assert.Equal(sensor.Id, Assert.Single(panel.Sources).Value.SensorId);
            }
            finally
            {
                dashboards.Dispose();
            }
        }

        // A subscription builds its candidate source before the panel decides. With a
        // plotted property other than the source's default the candidate is already
        // listening to the sensor's values; before the fix a rejected one was dropped
        // without being disposed, and its handler stayed on the sensor.
        [Fact]
        [Trait("Category", "Panel subscriptions")]
        public async Task CandidateThePanelRejects_ForANewSensor_LeavesNothingSubscribedToItsValues()
        {
            var (product, key) = await AddProductAsync(Guid.NewGuid());

            var dashboards = new DashboardManager(_databaseCoreManager.DatabaseCore, _valuesCache);

            try
            {
                var (_, panel) = await AddDashboardAsync(dashboards,
                    BuildPanel(folders: null, property: PlottedProperty.Mean, $"{product.DisplayName}/bars/*"));

                // The control: an accepted candidate listens exactly once.
                var accepted = await CreateSensorAsync(product.Id, key, "bars/accepted", SensorType.IntegerBar);

                Assert.Equal(accepted.Id, Assert.Single(panel.Sources).Value.SensorId);
                Assert.Single(accepted.ReceivedNewValue.GetInvocationList());

                // The panel plots integer bars now: a double bar is a type mismatch.
                var rejected = await CreateSensorAsync(product.Id, key, "bars/rejected", SensorType.DoubleBar);

                Assert.Single(panel.Sources);
                Assert.Null(rejected.ReceivedNewValue);
            }
            finally
            {
                dashboards.Dispose();
            }
        }

        // The same candidates, built when a scan is applied.
        [Fact]
        [Trait("Category", "Panel subscriptions")]
        public async Task CandidateThePanelRejects_WhenAScanIsApplied_LeavesNothingSubscribedToItsValues()
        {
            var (product, key) = await AddProductAsync(Guid.NewGuid());

            var dashboards = new DashboardManager(_databaseCoreManager.DatabaseCore, _valuesCache);

            try
            {
                // Not subscribed yet: the sensors below arrive unnoticed and are found by the scan.
                var (_, panel) = await AddDashboardAsync(dashboards,
                    BuildPanel(folders: null, property: PlottedProperty.Mean, subscribed: false, $"{product.DisplayName}/bars/*"));

                var integerBar = await CreateSensorAsync(product.Id, key, "bars/int", SensorType.IntegerBar);
                var doubleBar = await CreateSensorAsync(product.Id, key, "bars/double", SensorType.DoubleBar);

                Assert.Empty(panel.Sources);

                var subscription = Assert.Single(panel.Subscriptions).Value;

                await subscription.StartScanning(_ => new[] { integerBar, doubleBar });
                Assert.True(panel.TryApplyScanResults(subscription.Id, out var error));

                // The first one sets the panel's type, the second one does not fit it.
                Assert.Equal(integerBar.Id, Assert.Single(panel.Sources).Value.SensorId);
                Assert.Contains(nameof(SensorType.DoubleBar), error);

                Assert.Single(integerBar.ReceivedNewValue.GetInvocationList());
                Assert.Null(doubleBar.ReceivedNewValue);
            }
            finally
            {
                dashboards.Dispose();
            }
        }


        private async Task<(ProductModel Product, Guid Key)> AddProductAsync(Guid? folder)
        {
            var product = await _valuesCache.AddProductAsync($"root_{Guid.NewGuid():N}", Guid.Empty);

            // To the cache a folder is its id.
            if (folder is not null)
            {
                var result = await _valuesCache.UpdateProductAsync(new ProductUpdate { Id = product.Id, FolderId = folder }, default);
                Assert.True(result.IsOk, result.Error);
            }

            var key = _valuesCache.AddAccessKey(new AccessKeyModel(EntitiesFactory.BuildAccessKeyEntity(productId: product.Id.ToString()))).Id;

            return (product, key);
        }

        private async Task<BaseSensorModel> CreateSensorAsync(Guid productId, Guid key, string path, SensorType type = SensorType.Integer)
        {
            var value = SensorValuesFactory.BuildSensorValue(type, path, DateTime.UtcNow.AddMinutes(-1));
            var result = await _valuesCache.AddSensorValueAsync(key, productId, value);

            Assert.True(result.IsOk, result.Error);
            Assert.True(_valuesCache.TryGetSensorByPath(productId, path, out var sensor), $"sensor {path} was not created");

            return sensor;
        }

        // The way the UI does it: the dashboard is added to the store, then the panel is added to the dashboard.
        private static async Task<(Dashboard Dashboard, Panel Panel)> AddDashboardAsync(DashboardManager dashboards, DashboardPanelEntity panelEntity)
        {
            Assert.True(await dashboards.TryAdd(new DashboardAdd { Name = $"board_{Guid.NewGuid():N}", AuthorId = Guid.Empty }, out var dashboard));

            var panel = new Panel(panelEntity, dashboard);

            Assert.True(dashboard.TryAddPanel(panel));

            return (dashboard, panel);
        }

        // What a restart would find: the store read by another manager over the same cache.
        private List<Guid> ReloadSourceSensors(Guid dashboardId, Guid panelId)
        {
            var reloaded = new DashboardManager(_databaseCoreManager.DatabaseCore, _valuesCache);

            try
            {
                reloaded.Initialize();

                Assert.True(reloaded.TryGetValue(dashboardId, out var dashboard), "the dashboard is not in the store");
                Assert.True(dashboard.Panels.TryGetValue(panelId, out var panel), "the panel is not in the store");

                return panel.Sources.Select(s => s.Value.SensorId).ToList();
            }
            finally
            {
                reloaded.Dispose();
            }
        }

        private static DashboardPanelEntity BuildPanel(params string[] pathTemplates) =>
            BuildPanel(folders: null, property: PlottedProperty.Value, pathTemplates);

        private static DashboardPanelEntity BuildPanel(List<Guid> folders, PlottedProperty property, params string[] pathTemplates) =>
            BuildPanel(folders, property, subscribed: true, pathTemplates);

        private static DashboardPanelEntity BuildPanel(List<Guid> folders, PlottedProperty property, bool subscribed, params string[] pathTemplates) => new()
        {
            Id = Guid.NewGuid().ToByteArray(),
            Author = Guid.Empty.ToByteArray(),
            Name = string.Join(", ", pathTemplates),
            Subscriptions = pathTemplates.Select(template => new PanelSubscriptionEntity
            {
                Id = Guid.NewGuid().ToByteArray(),
                PathTemplate = template,
                Color = "Red", // the entity ctor parses the color name
                Property = (byte)property,
                Folders = folders,
                IsSubscribed = subscribed,
                IsApplied = subscribed,
            }).ToList(),
        };
    }
}
