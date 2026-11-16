using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.AccessManager.DatabaseEntities.SnapshotEntity;
using HSMSensorDataObjects.SensorValueRequests;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.DataLayer;
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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.TreeValuesCacheTests
{
    /// <summary>
    /// Sensor move (#1718) where it is easiest to break without noticing: a restart,
    /// a failed write, a concurrent structural change and the alias lifetime rules.
    /// The plain behaviour is in <see cref="SensorMoveTests"/> and <see cref="SensorMergeTests"/>.
    /// </summary>
    // The fixture database is shared by every test in the class, so each test
    // works under its own unique node name.
    [Collection("Database collection")]
    public class SensorMoveRobustnessTests : MonitoringCoreTestsBase<SensorMoveFixture>
    {
        private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(30);

        private readonly SensorMoveFixture _fixture;

        private FailingDatabaseCore _failing;


        public SensorMoveRobustnessTests(SensorMoveFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture, addTestProduct: false)
        {
            _fixture = fixture;
        }


        protected override IDatabaseCore WrapDatabase(IDatabaseCore inner) =>
            _failing = new FailingDatabaseCore(inner, _ => false);


        // === Structural changes that race with a move ===

        // The move holds the tree-structure lock across its entity write; a value that
        // arrives at the destination path meanwhile must wait for it and then land in the
        // moved sensor. Without the lock (or the re-check under it) it creates a second
        // sensor at the same path.
        [Fact]
        [Trait("Category", "Sensor move")]
        public async Task Move_AcrossProducts_AValueAtTheDestinationPathMeanwhile_LandsInTheMovedSensor()
        {
            var root = Unique();
            var sensor = await CreateIntSensorAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/src", values: 1);

            using var writeStarted = new ManualResetEventSlim();
            using var releaseWrite = new ManualResetEventSlim();

            _failing.ShouldFailSensorUpdate = entity =>
            {
                if (entity.Id == sensor.Id.ToString())
                {
                    writeStarted.Set();
                    releaseWrite.Wait(GateTimeout);
                }

                return false;
            };

            var time = DateTime.UtcNow.AddSeconds(5);
            Task<SensorMoveResult> move;
            Task<HSMCommon.TaskResult.TaskResult> add;

            try
            {
                move = _valuesCache.MoveSensorAsync(sensor.Id, _fixture.ProductB, $"{root}/dst");
                Assert.True(writeStarted.Wait(GateTimeout), "the move never reached its entity write");

                add = _valuesCache.AddSensorValueAsync(_fixture.KeyB, _fixture.ProductB,
                    new IntSensorValue { Path = $"{root}/dst", Time = time, Value = 42 });

                // The sensor is not published at the destination yet, so the value can only
                // be parked behind the move.
                Assert.NotSame(add, await Task.WhenAny(add, Task.Delay(300)));
            }
            finally
            {
                releaseWrite.Set();
                _failing.ShouldFailSensorUpdate = null;
            }

            Assert.Equal(SensorMoveOutcome.Moved, (await move).Outcome);
            Assert.True((await add).IsOk);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductB, $"{root}/dst", out var atDestination));
            Assert.Same(sensor, atDestination);
            Assert.Single(sensor.Parent.Sensors.Values, s => s.DisplayName == "dst");
            Assert.Equal(time, sensor.LastValue.Time);
            Assert.Equal(2, await CountHistoryRowsAsync(sensor.Id));
        }


        // === A failed entity write ===

        [Fact]
        [Trait("Category", "Sensor move")]
        public async Task Move_WithinProduct_WhenTheEntityWriteFails_RestoresTheSensorAndItsNode()
        {
            var root = Unique();
            var sensor = await CreateIntSensorAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/old/s", values: 1);
            var oldParent = sensor.Parent;

            var result = await MoveWithAFailingWriteAsync(sensor, _fixture.ProductA, $"{root}/new/s");

            Assert.Equal(SensorMoveOutcome.Failed, result.Outcome);
            Assert.Same(oldParent, sensor.Parent);
            Assert.True(oldParent.Sensors.ContainsKey(sensor.Id));
            Assert.Empty(sensor.Aliases);
            Assert.False(_valuesCache.TryGetSensorByPath(_fixture.ProductA, $"{root}/new/s", out _));

            // The old path is the sensor again, not an alias and not a hole.
            var time = DateTime.UtcNow.AddSeconds(5);
            await AddIntAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/old/s", time);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductA, $"{root}/old/s", out var stillThere));
            Assert.Same(sensor, stillThere);
            Assert.Equal(time, sensor.LastValue.Time);
        }

        // Moving back retires the alias of the path it lands on before the write: a failed
        // write must bring that alias back, or the old path would start a new sensor.
        [Fact]
        [Trait("Category", "Sensor alias")]
        public async Task MoveBack_WhenTheEntityWriteFails_KeepsTheAliasItWasAboutToRetire()
        {
            var root = Unique();
            var sensor = await CreateIntSensorAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/a", values: 1);
            Assert.True((await _valuesCache.MoveSensorAsync(sensor.Id, _fixture.ProductA, $"{root}/b")).IsOk);

            var result = await MoveWithAFailingWriteAsync(sensor, _fixture.ProductA, $"{root}/a");

            Assert.Equal(SensorMoveOutcome.Failed, result.Outcome);
            Assert.Equal($"{root}/b", sensor.Path);
            Assert.Equal($"{root}/a", Assert.Single(sensor.Aliases).Path);

            var time = DateTime.UtcNow.AddSeconds(5);
            await AddIntAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/a", time);

            Assert.False(_valuesCache.TryGetSensorByPath(_fixture.ProductA, $"{root}/a", out _), "the aliased path was recreated");
            Assert.Equal(time, sensor.LastValue.Time);
        }


        // === Restart ===

        [Fact]
        [Trait("Category", "Sensor alias")]
        public async Task Restart_DropsAnAlias_ThatARealSensorShadows_OrWhoseProductIsGone()
        {
            var root = Unique();
            var moved = await CreateIntSensorAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/old", values: 1);
            var real = await CreateIntSensorAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/real", values: 1);
            Assert.True((await _valuesCache.MoveSensorAsync(moved.Id, _fixture.ProductA, $"{root}/new")).IsOk);

            // What a crash between a displacement and its persist, or a product deletion,
            // leaves in the entity.
            var database = _databaseCoreManager.DatabaseCore;
            var entity = ReadEntity(moved.Id);
            var now = DateTime.UtcNow.Ticks;

            database.UpdateSensor(entity with
            {
                Aliases =
                [
                    .. entity.Aliases,
                    new SensorAliasEntity { ProductId = _fixture.ProductA.ToString(), Path = $"{root}/real", CreationDate = now },
                    new SensorAliasEntity { ProductId = Guid.NewGuid().ToString(), Path = $"{root}/gone", CreationDate = now },
                ],
            });

            await using var restarted = BuildCacheOverTheSameDatabase();

            Assert.True(restarted.TryGetSensorByPath(_fixture.ProductA, $"{root}/new", out var reloaded));
            Assert.Equal($"{root}/old", Assert.Single(reloaded.Aliases).Path);
            Assert.Equal($"{root}/old", Assert.Single(ReadEntity(moved.Id).Aliases).Path);

            Assert.True(restarted.TryGetSensorByPath(_fixture.ProductA, $"{root}/real", out var reloadedReal));
            Assert.Equal(real.Id, reloadedReal.Id);
        }


        // === Alias lifetime ===

        [Fact]
        [Trait("Category", "Sensor alias")]
        public async Task Alias_OfASensorThatNeverSelfDestroys_IsKeptHoweverOld()
        {
            var root = Unique();
            var sensor = await CreateIntSensorAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/old", values: 1);
            Assert.True((await _valuesCache.MoveSensorAsync(sensor.Id, _fixture.ProductA, $"{root}/new")).IsOk);
            await SetSelfDestroyAsync(_valuesCache, sensor.Id, new TimeIntervalModel(TimeInterval.None));

            BackdateAliases(sensor.Id, DateTime.UtcNow.AddYears(-10));
            await using var restarted = BuildCacheOverTheSameDatabase();
            Assert.True(restarted.TryGetSensorByPath(_fixture.ProductA, $"{root}/new", out var reloaded));

            await restarted.RunAliasMaintenanceAsync();
            Assert.Single(reloaded.Aliases);

            // The control: the same alias goes as soon as the sensor has an interval.
            await SetSelfDestroyAsync(restarted, sensor.Id, new TimeIntervalModel(TimeSpan.FromDays(1).Ticks));
            await restarted.RunAliasMaintenanceAsync();
            Assert.Empty(reloaded.Aliases);
        }

        [Fact]
        [Trait("Category", "Sensor alias")]
        public async Task Alias_Hit_ExtendsItsLifetime_AndIsWrittenAtMostOncePerHour()
        {
            var root = Unique();
            var sensor = await CreateIntSensorAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/old", values: 1);
            Assert.True((await _valuesCache.MoveSensorAsync(sensor.Id, _fixture.ProductA, $"{root}/new")).IsOk);
            await SetSelfDestroyAsync(_valuesCache, sensor.Id, new TimeIntervalModel(TimeSpan.FromDays(1).Ticks));

            // Created ten years ago: only a hit keeps it past the one-day interval.
            BackdateAliases(sensor.Id, DateTime.UtcNow.AddYears(-10));

            var writes = 0;
            var counting = new FailingDatabaseCore(_databaseCoreManager.DatabaseCore, _ => false)
            {
                ShouldFailSensorUpdate = entity =>
                {
                    if (entity.Id == sensor.Id.ToString())
                        Interlocked.Increment(ref writes);

                    return false;
                },
            };

            await using var restarted = BuildCacheOverTheSameDatabase(counting);
            Assert.True(restarted.TryGetSensorByPath(_fixture.ProductA, $"{root}/new", out var reloaded));

            await AddIntAsync(restarted, _fixture.ProductA, _fixture.KeyA, $"{root}/old", DateTime.UtcNow.AddSeconds(5));

            await restarted.RunAliasMaintenanceAsync();

            var alias = Assert.Single(reloaded.Aliases);
            Assert.Equal(alias.LastHit.Ticks, Assert.Single(ReadEntity(sensor.Id).Aliases).LastHit);
            Assert.Equal(1, Volatile.Read(ref writes));

            // A second hit inside the hour is not worth an entity write.
            await AddIntAsync(restarted, _fixture.ProductA, _fixture.KeyA, $"{root}/old", DateTime.UtcNow.AddSeconds(6));
            await restarted.RunAliasMaintenanceAsync();

            Assert.Single(reloaded.Aliases);
            Assert.Equal(1, Volatile.Read(ref writes));
        }

        [Fact]
        [Trait("Category", "Sensor alias")]
        public async Task Alias_RegistrationWithAnExplicitOtherType_DisplacesTheAlias()
        {
            var root = Unique();
            var sensor = await CreateIntSensorAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/old", values: 1);
            Assert.True((await _valuesCache.MoveSensorAsync(sensor.Id, _fixture.ProductA, $"{root}/new")).IsOk);

            var registration = new SensorAddOrUpdateRequest(_fixture.ProductA, $"{root}/old")
            {
                Update = new SensorUpdate { Id = Guid.Empty, Description = "a double sensor now" },
                Type = SensorType.Double,
                TypeIsExplicit = true,
            };

            Assert.True((await _valuesCache.AddOrUpdateSensorAsync(registration)).IsOk);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductA, $"{root}/old", out var created));
            Assert.NotEqual(sensor.Id, created.Id);
            Assert.Equal(SensorType.Double, created.Type);
            Assert.Equal("a double sensor now", created.Description);
            Assert.NotEqual("a double sensor now", sensor.Description);

            await TestWait.UntilAsync(() => sensor.Aliases.Count == 0, "the displaced alias left the moved sensor");
        }

        // === Templates ===

        // A template that matches the sensor before and after the move is left alone: the
        // same policy, no journal record but the move's own. Inside one product the
        // re-evaluation is done when the move returns.
        [Fact]
        [Trait("Category", "Sensor move")]
        public async Task Move_WithinProduct_LeavesAStillMatchingTemplateUntouched_AndDetachesAStaleOneAtOnce()
        {
            var root = Unique();

            var staying = BuildTtlTemplate(_fixture.FolderA, $"{SensorMoveFixture.ProductAName}/{root}/grp/*");
            Assert.True((await _valuesCache.AddAlertTemplateAsync(staying)).Success);

            try
            {
                var sensor = await CreateIntSensorAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/grp/s1", values: 1);
                var policyId = Assert.Single(sensor.Policies.TTLPolicies, t => t.TemplateId == staying.Id).Id;

                var records = new ConcurrentBag<JournalRecordModel>();
                void OnRecord(JournalRecordModel record) => records.Add(record);
                _journalService.NewRecordEvent += OnRecord;

                // Re-applying the template would write the sensor again.
                var writes = 0;
                _failing.ShouldFailSensorUpdate = entity =>
                {
                    if (entity.Id == sensor.Id.ToString())
                        Interlocked.Increment(ref writes);

                    return false;
                };

                try
                {
                    Assert.True((await _valuesCache.MoveSensorAsync(sensor.Id, _fixture.ProductA, $"{root}/grp/s2")).IsOk);
                }
                finally
                {
                    _failing.ShouldFailSensorUpdate = null;
                    _journalService.NewRecordEvent -= OnRecord;
                }

                Assert.Equal(policyId, Assert.Single(sensor.Policies.TTLPolicies, t => t.TemplateId == staying.Id).Id);
                Assert.Equal("Move sensor", Assert.Single(records, r => r.Path?.Contains(root) == true).Enviroment);
                Assert.Equal(1, Volatile.Read(ref writes));

                // Out of the template's path: detached by the time the move returns.
                Assert.True((await _valuesCache.MoveSensorAsync(sensor.Id, _fixture.ProductA, $"{root}/other/s2")).IsOk);
                Assert.DoesNotContain(sensor.Policies.TTLPolicies, t => t.TemplateId == staying.Id);
            }
            finally
            {
                await _valuesCache.RemoveAlertTemplateAsync(staying.Id);
            }
        }


        // === Merge ===

        // The copy is not atomic with the rest of the merge. If it finished and the merge
        // did not, the occupant is intact, the moved sensor already has the copied rows,
        // and a second attempt completes the merge without duplicating anything.
        [Fact]
        [Trait("Category", "Sensor merge")]
        public async Task Merge_ThatFailedAfterTheCopy_IsCompletedByASecondAttempt()
        {
            var root = Unique();
            var start = DateTime.UtcNow.AddMinutes(-30);

            var moved = await CreateIntSensorAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/a", start);
            var occupant = await CreateIntSensorAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/b", start.AddMinutes(1), start.AddMinutes(2));

            _failing.FailMergeAfterCopyFrom = id => id == occupant.Id;
            SensorMoveResult failed;
            try
            {
                failed = await _valuesCache.MoveSensorAsync(moved.Id, _fixture.ProductA, $"{root}/b", SensorMoveConflict.Merge);
            }
            finally
            {
                _failing.FailMergeAfterCopyFrom = null;
            }

            Assert.Equal(SensorMoveOutcome.Failed, failed.Outcome);
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductA, $"{root}/b", out var stillOccupant));
            Assert.Same(occupant, stillOccupant);
            Assert.Equal(2, await CountHistoryRowsAsync(occupant.Id));
            Assert.Equal($"{root}/a", moved.Path);
            Assert.Empty(moved.Aliases);

            var retried = await _valuesCache.MoveSensorAsync(moved.Id, _fixture.ProductA, $"{root}/b", SensorMoveConflict.Merge);

            Assert.Equal(SensorMoveOutcome.Merged, retried.Outcome);
            Assert.Equal(0, retried.MergedValues);
            Assert.Equal(2, retried.MergeCollisions);
            Assert.Null(_valuesCache.GetSensor(occupant.Id));
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductA, $"{root}/b", out var survivor));
            Assert.Same(moved, survivor);
            Assert.Equal(3, await CountHistoryRowsAsync(moved.Id));
            Assert.Equal(0, await CountHistoryRowsAsync(occupant.Id));

            // Nothing was copied this time, yet the newest value is the occupant's.
            Assert.Equal(start.AddMinutes(2), moved.LastValue.Time);
        }


        private static string Unique() => $"r{Guid.NewGuid():N}"[..12];

        private Task<BaseSensorModel> CreateIntSensorAsync(Guid productId, Guid key, string path, int values)
        {
            var start = DateTime.UtcNow.AddMinutes(-values);

            return CreateIntSensorAsync(productId, key, path, Enumerable.Range(0, values).Select(i => start.AddSeconds(i)).ToArray());
        }

        private async Task<BaseSensorModel> CreateIntSensorAsync(Guid productId, Guid key, string path, params DateTime[] times)
        {
            foreach (var time in times)
                await AddIntAsync(_valuesCache, productId, key, path, time);

            Assert.True(_valuesCache.TryGetSensorByPath(productId, path, out var sensor), $"sensor {path} was not created");

            return sensor;
        }

        private Task AddIntAsync(Guid productId, Guid key, string path, DateTime time) =>
            AddIntAsync(_valuesCache, productId, key, path, time);

        private static async Task AddIntAsync(TreeValuesCache cache, Guid productId, Guid key, string path, DateTime time)
        {
            var result = await cache.AddSensorValueAsync(key, productId, new IntSensorValue { Path = path, Time = time, Value = 1 });

            Assert.True(result.IsOk, result.Error);
        }

        private async Task<SensorMoveResult> MoveWithAFailingWriteAsync(BaseSensorModel sensor, Guid productId, string path)
        {
            _failing.ShouldFailSensorUpdate = entity => entity.Id == sensor.Id.ToString();

            try
            {
                return await _valuesCache.MoveSensorAsync(sensor.Id, productId, path);
            }
            finally
            {
                _failing.ShouldFailSensorUpdate = null;
            }
        }

        private static async Task SetSelfDestroyAsync(TreeValuesCache cache, Guid sensorId, TimeIntervalModel interval)
        {
            var result = await cache.UpdateSensorAsync(new SensorUpdate { Id = sensorId, SelfDestroy = interval });

            Assert.True(result.IsOk, result.Error);
        }

        private SensorEntity ReadEntity(Guid sensorId) =>
            _databaseCoreManager.DatabaseCore.GetAllSensors().Single(e => e.Id == sensorId.ToString());

        // Rewrites the stored aliases as if they were created (and never hit) at `created`;
        // a cache built over the database afterwards loads them that way.
        private void BackdateAliases(Guid sensorId, DateTime created)
        {
            var entity = ReadEntity(sensorId);

            _databaseCoreManager.DatabaseCore.UpdateSensor(entity with
            {
                Aliases = entity.Aliases.Select(a => a with { CreationDate = created.Ticks, LastHit = 0 }).ToList(),
            });
        }

        private async Task<int> CountHistoryRowsAsync(Guid sensorId)
        {
            var count = 0;

            await foreach (var _ in _databaseCoreManager.DatabaseCore.GetSensorValues(sensorId, DateTime.MinValue, DateTime.MaxValue))
                count++;

            return count;
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
