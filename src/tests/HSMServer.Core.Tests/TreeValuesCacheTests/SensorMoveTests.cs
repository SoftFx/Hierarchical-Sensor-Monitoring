using HSMCommon.Model;
using HSMServer.Core.DataLayer;
using HSMDatabase.AccessManager.DatabaseEntities.SnapshotEntity;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.NodeSettings;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Core.Tests.TreeValuesCacheTests.Fixture;
using HSMServer.Core.TreeStateSnapshot;
using HSMServer.Core.TreeStateSnapshot.States;
using Moq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.TreeValuesCacheTests
{
    /// <summary>
    /// Sensor move and aliases (#1718, ADR-0018): the sensor keeps its id, history,
    /// settings and policies; the old path keeps routing values to it.
    /// </summary>
    // The fixture database is shared by every test in the class, so each test
    // works under its own unique node name.
    [Collection("Database collection")]
    public class SensorMoveTests : MonitoringCoreTestsBase<SensorMoveFixture>
    {
        private readonly SensorMoveFixture _fixture;


        public SensorMoveTests(SensorMoveFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture, addTestProduct: false)
        {
            _fixture = fixture;
        }


        [Fact]
        [Trait("Category", "Sensor move")]
        public async Task Move_WithinProduct_KeepsIdAndHistory_AndCreatesMissingNodes()
        {
            var root = Unique();
            var sensor = await CreateIntSensorAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/old/cpu", values: 3);

            var result = await _valuesCache.MoveSensorAsync(sensor.Id, _fixture.ProductA, $"{root}/new/deep/cpu2");

            Assert.Equal(SensorMoveOutcome.Moved, result.Outcome);
            Assert.Equal($"{SensorMoveFixture.ProductAName}/{root}/old/cpu", result.OldPath);
            Assert.Equal($"{SensorMoveFixture.ProductAName}/{root}/new/deep/cpu2", result.NewPath);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductA, $"{root}/new/deep/cpu2", out var moved));
            Assert.Same(sensor, moved);
            Assert.False(_valuesCache.TryGetSensorByPath(_fixture.ProductA, $"{root}/old/cpu", out _));
            Assert.Equal("cpu2", moved.DisplayName);
            Assert.Equal("deep", moved.Parent.DisplayName);
            Assert.True(moved.Parent.Sensors.ContainsKey(moved.Id));

            // History is keyed by the sensor id: nothing was re-keyed.
            Assert.Equal(3, await CountHistoryRowsAsync(sensor.Id));

            var entity = _databaseCoreManager.DatabaseCore.GetAllSensors().Single(e => e.Id == sensor.Id.ToString());
            Assert.Equal(moved.Parent.Id.ToString(), entity.ProductId);
            Assert.Equal("cpu2", entity.DisplayName);
            Assert.Single(entity.Aliases);
            Assert.Equal($"{root}/old/cpu", entity.Aliases[0].Path);
            Assert.Equal(_fixture.ProductA.ToString(), entity.Aliases[0].ProductId);
        }

        [Fact]
        [Trait("Category", "Sensor move")]
        public async Task Move_AcrossProducts_ReRootsTheSensor_AndJournalsTheMove()
        {
            var root = Unique();
            var sensor = await CreateIntSensorAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/temp", values: 2);

            var records = new ConcurrentBag<JournalRecordModel>();
            void OnRecord(JournalRecordModel record) => records.Add(record);
            _journalService.NewRecordEvent += OnRecord;

            SensorMoveResult result;
            try
            {
                result = await _valuesCache.MoveSensorAsync(sensor.Id, _fixture.ProductB, $"{root}/temp");
            }
            finally
            {
                _journalService.NewRecordEvent -= OnRecord;
            }

            Assert.Equal(SensorMoveOutcome.Moved, result.Outcome);
            Assert.Equal(_fixture.ProductB, sensor.Root.Id);
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductB, $"{root}/temp", out var moved));
            Assert.Same(sensor, moved);
            Assert.False(_valuesCache.TryGetSensorByPath(_fixture.ProductA, $"{root}/temp", out _));
            Assert.Equal(2, await CountHistoryRowsAsync(sensor.Id));

            var record = Assert.Single(records, r => r.Enviroment == "Move sensor");
            Assert.Equal($"{SensorMoveFixture.ProductAName}/{root}/temp", record.OldValue);
            Assert.Equal($"{SensorMoveFixture.ProductBName}/{root}/temp", record.NewValue);
        }

        [Fact]
        [Trait("Category", "Sensor move")]
        public async Task Move_OntoAnExistingSensor_IsAConflict_AndChangesNothing()
        {
            var root = Unique();
            var sensor = await CreateIntSensorAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/a", values: 1);
            var occupant = await CreateIntSensorAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/b", values: 1);

            var result = await _valuesCache.MoveSensorAsync(sensor.Id, _fixture.ProductA, $"{root}/b");

            Assert.Equal(SensorMoveOutcome.Conflict, result.Outcome);
            Assert.Contains(occupant.Id.ToString(), result.Error);
            Assert.Equal($"{root}/a", sensor.Path);
            Assert.Empty(sensor.Aliases);
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductA, $"{root}/b", out var stillThere));
            Assert.Same(occupant, stillThere);
        }

        [Theory]
        [Trait("Category", "Sensor move")]
        [InlineData("")]
        [InlineData("a/b\tc")]
        [InlineData("a\\b")]
        [InlineData("1/2/3/4/5/6/7/8/9/10/11")]
        public async Task Move_ToAnInvalidPath_IsRejected(string path)
        {
            var sensor = await CreateIntSensorAsync(_fixture.ProductA, _fixture.KeyA, $"{Unique()}/s", values: 1);

            var result = await _valuesCache.MoveSensorAsync(sensor.Id, _fixture.ProductA, path);

            Assert.Equal(SensorMoveOutcome.InvalidPath, result.Outcome);
            Assert.Empty(sensor.Aliases);
        }

        [Fact]
        [Trait("Category", "Sensor move")]
        public async Task Move_UnknownSensorOrProduct_IsReported()
        {
            var sensor = await CreateIntSensorAsync(_fixture.ProductA, _fixture.KeyA, $"{Unique()}/s", values: 1);

            Assert.Equal(SensorMoveOutcome.SensorNotFound,
                (await _valuesCache.MoveSensorAsync(Guid.NewGuid(), _fixture.ProductA, "x")).Outcome);
            Assert.Equal(SensorMoveOutcome.DestinationNotFound,
                (await _valuesCache.MoveSensorAsync(sensor.Id, Guid.NewGuid(), "x")).Outcome);

            // A sub-node is not a destination product: paths are root-relative.
            Assert.Equal(SensorMoveOutcome.DestinationNotFound,
                (await _valuesCache.MoveSensorAsync(sensor.Id, sensor.Parent.Id, "x")).Outcome);
        }

        [Fact]
        [Trait("Category", "Sensor move")]
        public async Task Move_ToTheCurrentPath_IsUnchanged()
        {
            var root = Unique();
            var sensor = await CreateIntSensorAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/s", values: 1);

            var result = await _valuesCache.MoveSensorAsync(sensor.Id, _fixture.ProductA, $"/{root}/s");

            Assert.Equal(SensorMoveOutcome.Unchanged, result.Outcome);
            Assert.Empty(sensor.Aliases);
        }

        [Fact]
        [Trait("Category", "Sensor move")]
        public async Task Move_FromParentSettings_ResolveAgainstTheNewParent()
        {
            var root = Unique();
            var sensor = await CreateIntSensorAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/s", values: 1);

            var keepHistory = new TimeIntervalModel(TimeSpan.FromDays(3).Ticks);
            Assert.True((await _valuesCache.UpdateProductAsync(new ProductUpdate { Id = _fixture.ProductB, KeepHistory = keepHistory }, default)).IsOk);
            Assert.NotEqual(keepHistory.Ticks, sensor.Settings.KeepHistory.Value.Ticks);

            Assert.True((await _valuesCache.MoveSensorAsync(sensor.Id, _fixture.ProductB, $"{root}/s")).IsOk);

            Assert.False(sensor.Settings.KeepHistory.IsSet, "test premise: the sensor inherits KeepHistory");
            Assert.Equal(keepHistory.Ticks, sensor.Settings.KeepHistory.Value.Ticks);
        }

        [Fact]
        [Trait("Category", "Sensor move")]
        public async Task Move_KeepsOwnPolicies_AndReEvaluatesTemplates()
        {
            var root = Unique();

            var sourceTemplate = BuildTtlTemplate(_fixture.FolderA, $"{SensorMoveFixture.ProductAName}/{root}/src/*");
            var destinationTemplate = BuildTtlTemplate(_fixture.FolderB, $"{SensorMoveFixture.ProductBName}/{root}/dst/*");
            Assert.True((await _valuesCache.AddAlertTemplateAsync(sourceTemplate)).Success);
            Assert.True((await _valuesCache.AddAlertTemplateAsync(destinationTemplate)).Success);

            try
            {
                var sensor = await CreateIntSensorAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/src/s", values: 1);
                Assert.Contains(sensor.Policies.TTLPolicies, t => t.TemplateId == sourceTemplate.Id);

                var ownTtl = sensor.Policies.TTLPolicies.Where(t => t.TemplateId is null).Select(t => t.Id).ToList();

                Assert.True((await _valuesCache.MoveSensorAsync(sensor.Id, _fixture.ProductB, $"{root}/dst/s")).IsOk);

                // Across products the templates are re-evaluated on the destination's queue.
                await TestWait.UntilAsync(() => sensor.Policies.TTLPolicies.Any(t => t.TemplateId == destinationTemplate.Id),
                    "the destination template was applied");

                Assert.DoesNotContain(sensor.Policies.TTLPolicies, t => t.TemplateId == sourceTemplate.Id);
                Assert.Contains(sensor.Policies.TTLPolicies, t => t.TemplateId == destinationTemplate.Id);
                Assert.All(ownTtl, id => Assert.Contains(sensor.Policies.TTLPolicies, t => t.Id == id));
            }
            finally
            {
                await _valuesCache.RemoveAlertTemplateAsync(sourceTemplate.Id);
                await _valuesCache.RemoveAlertTemplateAsync(destinationTemplate.Id);
            }
        }


        [Fact]
        [Trait("Category", "Sensor alias")]
        public async Task Alias_WithinProduct_RedirectsValuesToTheMovedSensor()
        {
            var root = Unique();
            var sensor = await CreateIntSensorAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/old", values: 1);
            Assert.True((await _valuesCache.MoveSensorAsync(sensor.Id, _fixture.ProductA, $"{root}/new")).IsOk);

            var time = DateTime.UtcNow.AddSeconds(5);
            await AddIntAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/old", time);

            Assert.False(_valuesCache.TryGetSensorByPath(_fixture.ProductA, $"{root}/old", out _), "the old path was recreated");
            Assert.Equal(time, sensor.LastValue.Time);
            Assert.Equal(2, await CountHistoryRowsAsync(sensor.Id));
            Assert.True(sensor.Aliases.Single().LastHit > DateTime.MinValue);
        }

        [Fact]
        [Trait("Category", "Sensor alias")]
        public async Task Alias_AcrossProducts_DeliversThroughTheOwningProductsQueue()
        {
            var root = Unique();
            var sensor = await CreateIntSensorAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/old", values: 1);
            Assert.True((await _valuesCache.MoveSensorAsync(sensor.Id, _fixture.ProductB, $"{root}/new")).IsOk);

            var time = DateTime.UtcNow.AddSeconds(5);
            await AddIntAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/old", time);

            // The value hops to product B's queue: wait for it there.
            await TestWait.UntilAsync(() => sensor.LastValue?.Time == time, "the aliased value reached the moved sensor");
            Assert.False(_valuesCache.TryGetSensorByPath(_fixture.ProductA, $"{root}/old", out _), "the old path was recreated");
        }

        [Fact]
        [Trait("Category", "Sensor alias")]
        public async Task Alias_ValueOfAnotherType_DisplacesTheAlias()
        {
            var root = Unique();
            var sensor = await CreateIntSensorAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/old", values: 1);
            Assert.True((await _valuesCache.MoveSensorAsync(sensor.Id, _fixture.ProductA, $"{root}/new")).IsOk);

            var value = SensorValuesFactory.BuildSensorValue(SensorType.Double, $"{root}/old", DateTime.UtcNow);
            Assert.True((await _valuesCache.AddSensorValueAsync(_fixture.KeyA, _fixture.ProductA, value)).IsOk);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductA, $"{root}/old", out var created));
            Assert.NotEqual(sensor.Id, created.Id);
            Assert.Equal(SensorType.Double, created.Type);

            await TestWait.UntilAsync(() => sensor.Aliases.Count == 0, "the displaced alias left the moved sensor");
        }

        [Fact]
        [Trait("Category", "Sensor alias")]
        public async Task Alias_Registration_UpdatesTheMovedSensor()
        {
            var root = Unique();
            var sensor = await CreateIntSensorAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/old", values: 1);
            Assert.True((await _valuesCache.MoveSensorAsync(sensor.Id, _fixture.ProductA, $"{root}/new")).IsOk);

            var registration = new SensorAddOrUpdateRequest(_fixture.ProductA, $"{root}/old")
            {
                Update = new SensorUpdate { Id = Guid.Empty, Description = "registered through the alias" },
                Type = SensorType.Integer,
                TypeIsExplicit = true,
            };

            Assert.True((await _valuesCache.AddOrUpdateSensorAsync(registration)).IsOk);

            Assert.Equal("registered through the alias", sensor.Description);
            Assert.False(_valuesCache.TryGetSensorByPath(_fixture.ProductA, $"{root}/old", out _), "the old path was recreated");
        }

        [Fact]
        [Trait("Category", "Sensor alias")]
        public async Task Alias_RegistrationAcrossProducts_UpdatesTheMovedSensorOnItsOwnQueue()
        {
            var root = Unique();
            var sensor = await CreateIntSensorAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/old", values: 1);
            Assert.True((await _valuesCache.MoveSensorAsync(sensor.Id, _fixture.ProductB, $"{root}/new")).IsOk);

            var registration = new SensorAddOrUpdateRequest(_fixture.ProductA, $"{root}/old")
            {
                Update = new SensorUpdate { Id = Guid.Empty, Description = "registered across products" },
                Type = SensorType.Integer,
                TypeIsExplicit = true,
            };

            Assert.True((await _valuesCache.AddOrUpdateSensorAsync(registration)).IsOk);

            await TestWait.UntilAsync(() => sensor.Description == "registered across products",
                "the registration reached the moved sensor through product B's queue");
            Assert.False(_valuesCache.TryGetSensorByPath(_fixture.ProductA, $"{root}/old", out _), "the old path was recreated");
        }

        [Fact]
        [Trait("Category", "Sensor alias")]
        public async Task Alias_SurvivesARestart()
        {
            var root = Unique();
            var sensor = await CreateIntSensorAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/old", values: 1);
            Assert.True((await _valuesCache.MoveSensorAsync(sensor.Id, _fixture.ProductB, $"{root}/new")).IsOk);

            await using var restarted = BuildCacheOverTheSameDatabase();

            Assert.True(restarted.TryGetSensorByPath(_fixture.ProductB, $"{root}/new", out var reloaded));
            Assert.Equal(sensor.Id, reloaded.Id);
            Assert.Single(reloaded.Aliases);

            var time = DateTime.UtcNow.AddSeconds(5);
            var value = SensorValuesFactory.BuildSensorValue(SensorType.Integer, $"{root}/old", time);
            Assert.True((await restarted.AddSensorValueAsync(_fixture.KeyA, _fixture.ProductA, value)).IsOk);

            await TestWait.UntilAsync(() => reloaded.LastValue?.Time == time, "the aliased value reached the reloaded sensor");
            Assert.False(restarted.TryGetSensorByPath(_fixture.ProductA, $"{root}/old", out _));
        }

        [Fact]
        [Trait("Category", "Sensor alias")]
        public async Task Alias_ExpiresByTheSensorsSelfDestroyInterval()
        {
            var root = Unique();
            var sensor = await CreateIntSensorAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/old", values: 1);
            Assert.True((await _valuesCache.MoveSensorAsync(sensor.Id, _fixture.ProductA, $"{root}/new")).IsOk);

            // Not expired yet: SelfDestroy far away.
            await SetSensorSelfDestroyAsync(sensor, TimeSpan.FromDays(30));
            await _valuesCache.RunAliasMaintenanceAsync();
            Assert.Single(sensor.Aliases);

            await SetSensorSelfDestroyAsync(sensor, TimeSpan.FromMilliseconds(1));
            await Task.Delay(50);
            await _valuesCache.RunAliasMaintenanceAsync();

            Assert.Empty(sensor.Aliases);
            Assert.Null(_databaseCoreManager.DatabaseCore.GetAllSensors().Single(e => e.Id == sensor.Id.ToString()).Aliases);

            // With the alias gone the old path is free again.
            await AddIntAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/old", DateTime.UtcNow);
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductA, $"{root}/old", out var fresh));
            Assert.NotEqual(sensor.Id, fresh.Id);
        }

        [Fact]
        [Trait("Category", "Sensor alias")]
        public async Task Alias_IsRetired_WhenTheSensorMovesBack()
        {
            var root = Unique();
            var sensor = await CreateIntSensorAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/a", values: 1);

            Assert.True((await _valuesCache.MoveSensorAsync(sensor.Id, _fixture.ProductA, $"{root}/b")).IsOk);
            Assert.Equal(SensorMoveOutcome.Moved, (await _valuesCache.MoveSensorAsync(sensor.Id, _fixture.ProductA, $"{root}/a")).Outcome);

            var alias = Assert.Single(sensor.Aliases);
            Assert.Equal($"{root}/b", alias.Path);
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductA, $"{root}/a", out var back));
            Assert.Same(sensor, back);
        }

        [Fact]
        [Trait("Category", "Sensor alias")]
        public async Task Alias_OfAnotherSensor_IsDisplacedByAMoveOntoItsPath()
        {
            var root = Unique();
            var first = await CreateIntSensorAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/x", values: 1);
            var second = await CreateIntSensorAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/y", values: 1);

            Assert.True((await _valuesCache.MoveSensorAsync(first.Id, _fixture.ProductA, $"{root}/x2")).IsOk);
            Assert.True((await _valuesCache.MoveSensorAsync(second.Id, _fixture.ProductA, $"{root}/x")).IsOk);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductA, $"{root}/x", out var owner));
            Assert.Same(second, owner);
            await TestWait.UntilAsync(() => first.Aliases.Count == 0, "the displaced alias left the first sensor");
        }

        [Fact]
        [Trait("Category", "Sensor alias")]
        public async Task Alias_GoesAway_WithItsSensor_OrOnRequest()
        {
            var root = Unique();
            var removed = await CreateIntSensorAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/r", values: 1);
            var kept = await CreateIntSensorAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/k", values: 1);
            Assert.True((await _valuesCache.MoveSensorAsync(removed.Id, _fixture.ProductA, $"{root}/r2")).IsOk);
            Assert.True((await _valuesCache.MoveSensorAsync(kept.Id, _fixture.ProductA, $"{root}/k2")).IsOk);

            await _valuesCache.RemoveSensorAsync(removed.Id);
            Assert.True(await _valuesCache.RemoveSensorAliasAsync(kept.Id, _fixture.ProductA, $"{root}/k"));
            Assert.False(await _valuesCache.RemoveSensorAliasAsync(kept.Id, _fixture.ProductA, $"{root}/k"));
            Assert.Empty(kept.Aliases);

            await AddIntAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/r", DateTime.UtcNow);
            await AddIntAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/k", DateTime.UtcNow);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductA, $"{root}/r", out var r));
            Assert.NotEqual(removed.Id, r.Id);
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductA, $"{root}/k", out var k));
            Assert.NotEqual(kept.Id, k.Id);
        }


        [Fact]
        [Trait("Category", "Sensor move")]
        public async Task Move_WhenTheEntityWriteFails_RestoresTheSensorWhereItWas()
        {
            var root = Unique();
            var sensor = await CreateIntSensorAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/old", values: 1);

            var failing = new FailingDatabaseCore(_databaseCoreManager.DatabaseCore, _ => false)
            {
                ShouldFailSensorUpdate = e => e.Id == sensor.Id.ToString(),
            };
            await using var cache = BuildCacheOverTheSameDatabase(failing);
            Assert.True(cache.TryGetSensorByPath(_fixture.ProductA, $"{root}/old", out var loaded));
            var oldParent = loaded.Parent;

            var result = await cache.MoveSensorAsync(loaded.Id, _fixture.ProductB, $"{root}/new");

            Assert.Equal(SensorMoveOutcome.Failed, result.Outcome);
            Assert.True(cache.TryGetSensorByPath(_fixture.ProductA, $"{root}/old", out var stillThere));
            Assert.Same(loaded, stillThere);
            Assert.Same(oldParent, loaded.Parent);
            Assert.Equal("old", loaded.DisplayName);
            Assert.Empty(loaded.Aliases);
            Assert.False(cache.TryGetSensorByPath(_fixture.ProductB, $"{root}/new", out _));
        }

        [Fact]
        [Trait("Category", "Sensor alias")]
        public async Task Alias_HitIsNotMarkedPersisted_WhenTheWriteFails()
        {
            var root = Unique();
            var sensor = await CreateIntSensorAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/old", values: 1);
            Assert.True((await _valuesCache.MoveSensorAsync(sensor.Id, _fixture.ProductA, $"{root}/new")).IsOk);

            var fail = true;
            var failing = new FailingDatabaseCore(_databaseCoreManager.DatabaseCore, _ => false)
            {
                ShouldFailSensorUpdate = e => fail && e.Id == sensor.Id.ToString(),
            };
            await using var cache = BuildCacheOverTheSameDatabase(failing);
            Assert.True(cache.TryGetSensorByPath(_fixture.ProductA, $"{root}/new", out var loaded));

            var value = SensorValuesFactory.BuildSensorValue(SensorType.Integer, $"{root}/old", DateTime.UtcNow);
            Assert.True((await cache.AddSensorValueAsync(_fixture.KeyA, _fixture.ProductA, value)).IsOk);
            var alias = loaded.Aliases.Single();
            Assert.True(alias.HasUnpersistedHit);

            await cache.RunAliasMaintenanceAsync();
            Assert.True(alias.HasUnpersistedHit, "a failed write marked the hit as persisted");

            fail = false;
            await cache.RunAliasMaintenanceAsync();
            Assert.False(alias.HasUnpersistedHit);

            var stored = _databaseCoreManager.DatabaseCore.GetAllSensors().Single(e => e.Id == sensor.Id.ToString());
            Assert.Equal(alias.LastHit.Ticks, stored.Aliases.Single().LastHit);
        }

        [Fact]
        [Trait("Category", "Sensor alias")]
        public async Task Alias_RegistrationWithoutAType_UpdatesTheMovedSensor_AndKeepsTheAlias()
        {
            var root = Unique();
            var sensor = await CreateIntSensorAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/old", values: 1);
            Assert.True((await _valuesCache.MoveSensorAsync(sensor.Id, _fixture.ProductA, $"{root}/new")).IsOk);

            // The request type defaults to something other than the sensor's;
            // only an explicit type may displace the alias.
            var registration = new SensorAddOrUpdateRequest(_fixture.ProductA, $"{root}/old")
            {
                Update = new SensorUpdate { Id = Guid.Empty, Description = "untyped registration" },
                Type = SensorType.Double,
                TypeIsExplicit = false,
            };

            Assert.True((await _valuesCache.AddOrUpdateSensorAsync(registration)).IsOk);

            Assert.Equal("untyped registration", sensor.Description);
            Assert.Single(sensor.Aliases);
            Assert.False(_valuesCache.TryGetSensorByPath(_fixture.ProductA, $"{root}/old", out _), "the old path was recreated");
        }


        private static string Unique() => $"n{Guid.NewGuid():N}"[..12];

        private async Task<BaseSensorModel> CreateIntSensorAsync(Guid productId, Guid key, string path, int values)
        {
            var start = DateTime.UtcNow.AddMinutes(-values);

            for (var i = 0; i < values; i++)
                await AddIntAsync(productId, key, path, start.AddSeconds(i));

            Assert.True(_valuesCache.TryGetSensorByPath(productId, path, out var sensor), $"sensor {path} was not created");

            return sensor;
        }

        private async Task AddIntAsync(Guid productId, Guid key, string path, DateTime time)
        {
            var value = SensorValuesFactory.BuildSensorValue(SensorType.Integer, path, time);
            var result = await _valuesCache.AddSensorValueAsync(key, productId, value);

            Assert.True(result.IsOk, result.Error);
        }

        private async Task<int> CountHistoryRowsAsync(Guid sensorId)
        {
            var count = 0;

            await foreach (var _ in _databaseCoreManager.DatabaseCore.GetSensorValues(sensorId, DateTime.MinValue, DateTime.MaxValue))
                count++;

            return count;
        }

        private async Task SetSensorSelfDestroyAsync(BaseSensorModel sensor, TimeSpan interval)
        {
            var result = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                SelfDestroy = new TimeIntervalModel(interval.Ticks),
            });

            Assert.True(result.IsOk, result.Error);
        }

        private AlertTemplateModel BuildTtlTemplate(Guid folderId, string pathPattern)
        {
            var ttlSetting = new TimeIntervalSettingProperty();
            ttlSetting.TrySetValue(new TimeIntervalModel(TimeSpan.FromMinutes(5).Ticks));

            var template = new AlertTemplateModel
            {
                Name = $"Move template {Guid.NewGuid():N}",
                FolderId = folderId,
                SensorType = (byte)SensorType.Integer,
                Paths = [pathPattern],
                TtlEntries = [new TtlEntry(new TTLPolicy(ttlSetting, null), ttlSetting.Value ?? TimeIntervalModel.None)],
            };
            template.TryApplyPathTemplates(out _);

            return template;
        }

        private TreeValuesCache BuildCacheOverTheSameDatabase(IDatabaseCore database = null)
        {
            var snapshot = new Mock<ITreeStateSnapshot>();
            snapshot.Setup(a => a.Keys).Returns(new StateCollection<LastKeyState, LastKeyStateEntity>());

            database ??= _databaseCoreManager.DatabaseCore;

            return new TreeValuesCache(database, snapshot.Object, _journalService, _alertScheduleProvider,
                new HSMServer.Core.ChangeSets.ChangeSetJournal(database));
        }
    }
}
