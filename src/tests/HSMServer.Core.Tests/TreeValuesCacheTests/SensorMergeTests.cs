using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities.SnapshotEntity;
using HSMServer.Core.Cache;
using HSMServer.Core.TreeStateSnapshot;
using HSMServer.Core.TreeStateSnapshot.States;
using Moq;
using HSMDatabase.AccessManager.Formatters;
using HSMSensorDataObjects.SensorValueRequests;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Core.Tests.TreeValuesCacheTests.Fixture;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.TreeValuesCacheTests
{
    /// <summary>
    /// Sensor move onto an occupied path with merge (#1720): the moved sensor
    /// survives and takes over the occupant's history; its own row wins a
    /// same-tick collision; the occupant is removed.
    /// </summary>
    [Collection("Database collection")]
    public class SensorMergeTests : MonitoringCoreTestsBase<SensorMoveFixture>
    {
        private static readonly MemoryPackFormatter _formatter = new();

        private readonly SensorMoveFixture _fixture;

        private FailingDatabaseCore _failing;


        public SensorMergeTests(SensorMoveFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture, addTestProduct: false)
        {
            _fixture = fixture;
        }


        [Fact]
        [Trait("Category", "Sensor merge")]
        public async Task Merge_TakesOverTheOccupantsHistory_AndRemovesIt()
        {
            var root = Unique();
            var start = DateTime.UtcNow.AddHours(-1);

            var moved = await CreateAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/x/s", start, start.AddMinutes(1));
            var occupant = await CreateAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/y/s", start.AddMinutes(10), start.AddMinutes(11), start.AddMinutes(12));

            var result = await _valuesCache.MoveSensorAsync(moved.Id, _fixture.ProductA, $"{root}/y/s", SensorMoveConflict.Merge);

            Assert.Equal(SensorMoveOutcome.Merged, result.Outcome);
            Assert.True(result.IsOk);
            Assert.Equal(occupant.Id, result.MergedSensorId);
            Assert.Equal(3, result.MergedValues);
            Assert.Equal(0, result.MergeCollisions);

            Assert.Null(_valuesCache.GetSensor(occupant.Id));
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductA, $"{root}/y/s", out var owner));
            Assert.Same(moved, owner);
            Assert.DoesNotContain(owner.Parent.Sensors.Values, s => s.Id == occupant.Id);

            Assert.Equal(5, (await ReadHistoryAsync(moved.Id)).Count);
            Assert.Empty(await ReadHistoryAsync(occupant.Id));

            // The newest value is now one of the absorbed rows.
            Assert.Equal(start.AddMinutes(12), moved.LastValue.Time);
        }

        [Fact]
        [Trait("Category", "Sensor merge")]
        public async Task Merge_SameTick_KeepsTheMovedSensorsRow()
        {
            var root = Unique();
            var tick = DateTime.UtcNow.AddMinutes(-30);

            var moved = await CreateAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/a", (tick, 111));
            var occupant = await CreateAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/b", (tick, 222), (tick.AddSeconds(1), 333));

            var result = await _valuesCache.MoveSensorAsync(moved.Id, _fixture.ProductA, $"{root}/b", SensorMoveConflict.Merge);

            Assert.Equal(SensorMoveOutcome.Merged, result.Outcome);
            Assert.Equal(1, result.MergedValues);
            Assert.Equal(1, result.MergeCollisions);
            Assert.Null(_valuesCache.GetSensor(occupant.Id));

            var history = await ReadHistoryAsync(moved.Id);
            Assert.Equal([111, 333], history.Select(v => ((IntegerValue)v).Value));
        }

        [Fact]
        [Trait("Category", "Sensor merge")]
        public async Task Merge_OfDifferentTypes_IsAConflict()
        {
            var root = Unique();
            var moved = await CreateAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/int", DateTime.UtcNow.AddMinutes(-1));

            var value = SensorValuesFactory.BuildSensorValue(SensorType.Double, $"{root}/double", DateTime.UtcNow);
            Assert.True((await _valuesCache.AddSensorValueAsync(_fixture.KeyA, _fixture.ProductA, value)).IsOk);
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductA, $"{root}/double", out var occupant));

            var result = await _valuesCache.MoveSensorAsync(moved.Id, _fixture.ProductA, $"{root}/double", SensorMoveConflict.Merge);

            Assert.Equal(SensorMoveOutcome.Conflict, result.Outcome);
            Assert.Contains("different types", result.Error);
            Assert.NotNull(_valuesCache.GetSensor(occupant.Id));
            Assert.Equal($"{root}/int", moved.Path);
        }

        [Fact]
        [Trait("Category", "Sensor merge")]
        public async Task Merge_AcrossProducts_AdoptsTheOccupantsAliases_AndOwnsThePath()
        {
            var root = Unique();
            var start = DateTime.UtcNow.AddHours(-1);

            var moved = await CreateAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/s", start);

            // The occupant itself was moved once, so it carries an alias in B.
            var occupant = await CreateAsync(_fixture.ProductB, _fixture.KeyB, $"{root}/before", start.AddMinutes(5));
            Assert.True((await _valuesCache.MoveSensorAsync(occupant.Id, _fixture.ProductB, $"{root}/s")).IsOk);

            var result = await _valuesCache.MoveSensorAsync(moved.Id, _fixture.ProductB, $"{root}/s", SensorMoveConflict.Merge);

            Assert.Equal(SensorMoveOutcome.Merged, result.Outcome);
            Assert.Null(_valuesCache.GetSensor(occupant.Id));
            Assert.Equal(_fixture.ProductB, moved.Root.Id);
            Assert.Contains(moved.Aliases, a => a.ProductId == _fixture.ProductB && a.Path == $"{root}/before");
            Assert.Contains(moved.Aliases, a => a.ProductId == _fixture.ProductA && a.Path == $"{root}/s");

            // The path, the occupant's alias and the moved sensor's own alias all land in it.
            var t1 = DateTime.UtcNow.AddSeconds(1);
            var t2 = DateTime.UtcNow.AddSeconds(2);
            var t3 = DateTime.UtcNow.AddSeconds(3);
            await AddAsync(_fixture.ProductB, _fixture.KeyB, $"{root}/s", t1, 1);
            await AddAsync(_fixture.ProductB, _fixture.KeyB, $"{root}/before", t2, 2);
            await AddAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/s", t3, 3);

            // t3 hops to product B's queue; its row is written after the cache
            // update, so wait on the stored history, not on LastValue.
            await TestWait.UntilAsync(async () => (await ReadHistoryAsync(moved.Id)).Count == 5,
                "every path's value was stored in the surviving sensor");
            Assert.Equal(t3, moved.LastValue.Time);
            Assert.False(_valuesCache.TryGetSensorByPath(_fixture.ProductB, $"{root}/before", out _));
        }

        [Fact]
        [Trait("Category", "Sensor merge")]
        public async Task Merge_WhenTheCopyFails_KeepsTheOccupantAndTheSensor()
        {
            var root = Unique();
            var start = DateTime.UtcNow.AddMinutes(-30);

            var moved = await CreateAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/a", start);
            var occupant = await CreateAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/b", start.AddMinutes(1), start.AddMinutes(2));

            _failing.FailMergeFrom = id => id == occupant.Id;
            SensorMoveResult result;
            try
            {
                result = await _valuesCache.MoveSensorAsync(moved.Id, _fixture.ProductA, $"{root}/b", SensorMoveConflict.Merge);
            }
            finally
            {
                _failing.FailMergeFrom = null;
            }

            Assert.Equal(SensorMoveOutcome.Failed, result.Outcome);
            Assert.Contains("injected", result.Error);

            // Nothing moved, nothing lost.
            Assert.Same(occupant, _valuesCache.GetSensor(occupant.Id));
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductA, $"{root}/b", out var stillOccupant));
            Assert.Same(occupant, stillOccupant);
            Assert.Equal(2, (await ReadHistoryAsync(occupant.Id)).Count);
            Assert.Equal($"{root}/a", moved.Path);
            Assert.Empty(moved.Aliases);
        }

        [Fact]
        [Trait("Category", "Sensor merge")]
        public async Task Merge_IntoAFreePath_IsAPlainMove()
        {
            var root = Unique();
            var moved = await CreateAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/a", DateTime.UtcNow.AddMinutes(-1));

            var result = await _valuesCache.MoveSensorAsync(moved.Id, _fixture.ProductA, $"{root}/free", SensorMoveConflict.Merge);

            Assert.Equal(SensorMoveOutcome.Moved, result.Outcome);
            Assert.Null(result.MergedSensorId);
        }


        [Fact]
        [Trait("Category", "Sensor merge")]
        public async Task Merge_WhenTheOccupantsEntityCannotBeRemoved_SaysSo()
        {
            var root = Unique();
            var start = DateTime.UtcNow.AddMinutes(-30);

            var moved = await CreateAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/a", start);
            var occupant = await CreateAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/b", start.AddMinutes(1));

            _failing.FailRemoveSensor = id => id == occupant.Id;
            SensorMoveResult result;
            try
            {
                result = await _valuesCache.MoveSensorAsync(moved.Id, _fixture.ProductA, $"{root}/b", SensorMoveConflict.Merge);
            }
            finally
            {
                _failing.FailRemoveSensor = null;
            }

            // The history is merged and the path is the moved sensor's, but the
            // leftover entity would come back on restart: the caller must hear it.
            Assert.Equal(SensorMoveOutcome.Merged, result.Outcome);
            Assert.NotNull(result.Error);
            Assert.Contains(occupant.Id.ToString(), result.Error);
            Assert.Contains("after a restart", result.Error);
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductA, $"{root}/b", out var owner));
            Assert.Same(moved, owner);
        }

        [Fact]
        [Trait("Category", "Sensor merge")]
        public async Task Merge_WhenTheMoveFailsAfterTheAbsorb_ReportsIt_AndPersistsTheAliases()
        {
            var root = Unique();
            var start = DateTime.UtcNow.AddMinutes(-30);

            var moved = await CreateAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/a", start);
            var occupant = await CreateAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/b", start.AddMinutes(1), start.AddMinutes(2));

            // Only the move's own entity write fails; the forced persist after it succeeds.
            var failures = 1;
            _failing.ShouldFailSensorUpdate = e => e.Id == moved.Id.ToString() && failures-- > 0;
            SensorMoveResult result;
            try
            {
                result = await _valuesCache.MoveSensorAsync(moved.Id, _fixture.ProductA, $"{root}/b", SensorMoveConflict.Merge);
            }
            finally
            {
                _failing.ShouldFailSensorUpdate = null;
            }

            Assert.Equal(SensorMoveOutcome.Failed, result.Outcome);
            Assert.Equal(occupant.Id, result.MergedSensorId);
            Assert.Equal(2, result.MergedValues);
            Assert.Contains("retry the move", result.Error);

            // The sensor stayed where it was, the occupant is gone, and its path
            // already routes to the sensor - in memory and after a restart.
            Assert.Equal($"{root}/a", moved.Path);
            Assert.Null(_valuesCache.GetSensor(occupant.Id));
            Assert.Equal(3, (await ReadHistoryAsync(moved.Id)).Count);

            await using var restarted = BuildCacheOverTheSameDatabase();
            var reloaded = restarted.GetSensor(moved.Id);
            Assert.Contains(reloaded.Aliases, a => a.ProductId == _fixture.ProductA && a.Path == $"{root}/b");
            Assert.False(restarted.TryGetSensorByPath(_fixture.ProductA, $"{root}/b", out _));
        }

        [Fact]
        [Trait("Category", "Sensor merge")]
        public async Task Merge_OfBarSensors_TakesOverTheOccupantsInProgressBar()
        {
            var root = Unique();
            var start = DateTime.UtcNow.AddHours(-2);

            var moved = await CreateBarAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/a", start);
            var occupant = await CreateBarAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/b", start.AddMinutes(10), start.AddMinutes(20));

            var result = await _valuesCache.MoveSensorAsync(moved.Id, _fixture.ProductA, $"{root}/b", SensorMoveConflict.Merge);

            Assert.Equal(SensorMoveOutcome.Merged, result.Outcome);
            Assert.True(result.IsOk, result.Error);

            // Both occupant bars, the in-progress one included, are now the survivor's rows.
            var history = await ReadHistoryAsync(moved.Id);
            Assert.Contains(history, v => v is BarBaseValue bar && bar.OpenTime == start.AddMinutes(10));
            Assert.Contains(history, v => v is BarBaseValue bar && bar.OpenTime == start.AddMinutes(20));
            Assert.Empty(await ReadHistoryAsync(occupant.Id));
        }


        protected override HSMServer.Core.DataLayer.IDatabaseCore WrapDatabase(HSMServer.Core.DataLayer.IDatabaseCore inner) =>
            _failing = new FailingDatabaseCore(inner, _ => false);

        // === Collector PreviousPaths (#1724): ApplyPreviousPathsAsync ===

        [Fact]
        [Trait("Category", "Sensor move from a collector")]
        public async Task PreviousPath_MovesTheOldSensor_WhenTheNewPathIsFree_AndIsANoOpAfterwards()
        {
            var root = Unique();
            var old = await CreateAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/old/cpu", DateTime.UtcNow.AddMinutes(-5));

            var result = await _valuesCache.ApplyPreviousPathsAsync(_fixture.KeyA, _fixture.ProductA, $"{root}/new/cpu",
                [$"{root}/old/cpu"], SensorType.Integer, HSMServer.Core.TableOfChanges.InitiatorInfo.AsCollector("key", false));

            Assert.True(result.IsOk, result.Error);
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductA, $"{root}/new/cpu", out var moved));
            Assert.Same(old, moved);
            Assert.Contains(old.Aliases, a => a.Path == $"{root}/old/cpu");

            // Every later registration repeats the list: the old path is an alias now, nothing moves.
            var again = await _valuesCache.ApplyPreviousPathsAsync(_fixture.KeyA, _fixture.ProductA, $"{root}/new/cpu",
                [$"{root}/old/cpu"], SensorType.Integer, HSMServer.Core.TableOfChanges.InitiatorInfo.AsCollector("key", false));
            Assert.True(again.IsOk);
            Assert.Equal($"{root}/new/cpu", old.Path);
        }

        [Fact]
        [Trait("Category", "Sensor move from a collector")]
        public async Task PreviousPath_MergesIntoASensorTheNewLayoutAlreadyCreated_TheOldOneSurvives()
        {
            var root = Unique();
            var start = DateTime.UtcNow.AddHours(-2);
            var old = await CreateAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/garage-server/cpu", start, start.AddMinutes(1));
            var fresh = await CreateAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/garage/cpu", start.AddHours(1));

            var result = await _valuesCache.ApplyPreviousPathsAsync(_fixture.KeyA, _fixture.ProductA, $"{root}/garage/cpu",
                [$"{root}/garage-server/cpu"], SensorType.Integer, HSMServer.Core.TableOfChanges.InitiatorInfo.AsCollector("key", false));

            Assert.True(result.IsOk, result.Error);
            Assert.Null(_valuesCache.GetSensor(fresh.Id));
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductA, $"{root}/garage/cpu", out var survivor));
            Assert.Same(old, survivor);
            Assert.Equal(3, (await ReadHistoryAsync(old.Id)).Count);
        }

        [Fact]
        [Trait("Category", "Sensor move from a collector")]
        public async Task PreviousPaths_TheFirstListedPathSurvives()
        {
            var root = Unique();
            var start = DateTime.UtcNow.AddHours(-2);
            var first = await CreateAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/v1/cpu", start);
            var second = await CreateAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/v2/cpu", start.AddMinutes(1));

            var result = await _valuesCache.ApplyPreviousPathsAsync(_fixture.KeyA, _fixture.ProductA, $"{root}/v3/cpu",
                [$"{root}/v1/cpu", $"{root}/v2/cpu"], SensorType.Integer, HSMServer.Core.TableOfChanges.InitiatorInfo.AsCollector("key", false));

            Assert.True(result.IsOk, result.Error);
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductA, $"{root}/v3/cpu", out var survivor));
            Assert.Same(first, survivor);
            Assert.Null(_valuesCache.GetSensor(second.Id));
            Assert.Equal(2, (await ReadHistoryAsync(first.Id)).Count);
        }

        [Fact]
        [Trait("Category", "Sensor move from a collector")]
        public async Task PreviousPath_OfAnotherType_IsSkipped()
        {
            var root = Unique();
            var old = await CreateAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/old", DateTime.UtcNow.AddMinutes(-1));

            var result = await _valuesCache.ApplyPreviousPathsAsync(_fixture.KeyA, _fixture.ProductA, $"{root}/new",
                [$"{root}/old"], SensorType.Double, HSMServer.Core.TableOfChanges.InitiatorInfo.AsCollector("key", false));

            Assert.True(result.IsOk);
            Assert.Equal($"{root}/old", old.Path);
            Assert.False(_valuesCache.TryGetSensorByPath(_fixture.ProductA, $"{root}/new", out _));
        }

        [Fact]
        [Trait("Category", "Sensor move from a collector")]
        public async Task PreviousPaths_WithoutARegistrationType_MoveOnlyTheFirstListedType()
        {
            var root = Unique();
            var first = await CreateAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/int", DateTime.UtcNow.AddMinutes(-2));
            var value = SensorValuesFactory.BuildSensorValue(SensorType.Double, $"{root}/double", DateTime.UtcNow.AddMinutes(-1));
            Assert.True((await _valuesCache.AddSensorValueAsync(_fixture.KeyA, _fixture.ProductA, value)).IsOk);
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductA, $"{root}/double", out var other));

            var result = await _valuesCache.ApplyPreviousPathsAsync(_fixture.KeyA, _fixture.ProductA, $"{root}/new",
                [$"{root}/int", $"{root}/double"], null, HSMServer.Core.TableOfChanges.InitiatorInfo.AsCollector("key", false));

            Assert.True(result.IsOk);
            Assert.Equal($"{root}/new", first.Path);
            Assert.Equal($"{root}/double", other.Path);
        }

        [Fact]
        [Trait("Category", "Sensor move from a collector")]
        public async Task PreviousPath_OfAnotherProduct_IsNotReachable()
        {
            var root = Unique();
            var other = await CreateAsync(_fixture.ProductB, _fixture.KeyB, $"{root}/old", DateTime.UtcNow.AddMinutes(-1));

            // Key A, product A: the path is looked up in product A only.
            var result = await _valuesCache.ApplyPreviousPathsAsync(_fixture.KeyA, _fixture.ProductA, $"{root}/new",
                [$"{root}/old"], SensorType.Integer, HSMServer.Core.TableOfChanges.InitiatorInfo.AsCollector("key", false));

            Assert.True(result.IsOk);
            Assert.Equal(_fixture.ProductB, other.Root.Id);
            Assert.Equal($"{root}/old", other.Path);
        }


        [Fact]
        [Trait("Category", "Sensor move from a collector")]
        public async Task PreviousPath_OfABlockedSensor_IsSkipped_AndTheLiveSensorStays()
        {
            var root = Unique();
            var start = DateTime.UtcNow.AddHours(-2);
            var retired = await CreateAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/old/cpu", start);
            var live = await CreateAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/new/cpu", start.AddHours(1));

            var blocked = await _valuesCache.UpdateSensorAsync(new HSMServer.Core.Cache.UpdateEntities.SensorUpdate
            {
                Id = retired.Id,
                State = SensorState.Blocked,
            });
            Assert.True(blocked.IsOk, blocked.Error);

            var result = await _valuesCache.ApplyPreviousPathsAsync(_fixture.KeyA, _fixture.ProductA, $"{root}/new/cpu",
                [$"{root}/old/cpu"], SensorType.Integer, HSMServer.Core.TableOfChanges.InitiatorInfo.AsCollector("key", false));

            Assert.True(result.IsOk);
            Assert.Equal($"{root}/old/cpu", retired.Path);
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductA, $"{root}/new/cpu", out var owner));
            Assert.Same(live, owner);
            Assert.NotEqual(SensorState.Blocked, live.State);
        }

        [Fact]
        [Trait("Category", "Sensor move from a collector")]
        public async Task PreviousPaths_BeyondTheLimit_AreNotLookedAt()
        {
            var root = Unique();
            var late = await CreateAsync(_fixture.ProductA, _fixture.KeyA, $"{root}/late", DateTime.UtcNow.AddMinutes(-1));

            var list = Enumerable.Range(0, HSMServer.Core.Cache.TreeValuesCache.MaxPreviousPaths).Select(i => $"{root}/missing{i}").ToList();
            list.Add($"{root}/late");

            var result = await _valuesCache.ApplyPreviousPathsAsync(_fixture.KeyA, _fixture.ProductA, $"{root}/new",
                list, SensorType.Integer, HSMServer.Core.TableOfChanges.InitiatorInfo.AsCollector("key", false));

            Assert.True(result.IsOk);
            Assert.Equal($"{root}/late", late.Path);
        }


        private static string Unique() => $"m{Guid.NewGuid():N}"[..12];

        private Task<BaseSensorModel> CreateAsync(Guid productId, Guid key, string path, params DateTime[] times) =>
            CreateAsync(productId, key, path, times.Select((t, i) => (t, i)).ToArray());

        private async Task<BaseSensorModel> CreateAsync(Guid productId, Guid key, string path, params (DateTime Time, int Value)[] values)
        {
            foreach (var (time, value) in values)
                await AddAsync(productId, key, path, time, value);

            Assert.True(_valuesCache.TryGetSensorByPath(productId, path, out var sensor), $"sensor {path} was not created");

            return sensor;
        }

        private async Task AddAsync(Guid productId, Guid key, string path, DateTime time, int value)
        {
            var result = await _valuesCache.AddSensorValueAsync(key, productId,
                new IntSensorValue { Path = path, Time = time, Value = value });

            Assert.True(result.IsOk, result.Error);
        }

        private async Task<BaseSensorModel> CreateBarAsync(Guid productId, Guid key, string path, params DateTime[] openTimes)
        {
            foreach (var open in openTimes)
            {
                var result = await _valuesCache.AddSensorValueAsync(key, productId, new IntBarSensorValue
                {
                    Path = path,
                    OpenTime = open,
                    CloseTime = open.AddMinutes(5),
                    Time = open.AddMinutes(5),
                    Count = 2,
                    Min = 1,
                    Max = 3,
                    Mean = 2,
                    LastValue = 3,
                });

                Assert.True(result.IsOk, result.Error);
            }

            Assert.True(_valuesCache.TryGetSensorByPath(productId, path, out var sensor), $"bar sensor {path} was not created");

            return sensor;
        }

        private TreeValuesCache BuildCacheOverTheSameDatabase()
        {
            var snapshot = new Mock<ITreeStateSnapshot>();
            snapshot.Setup(a => a.Keys).Returns(new StateCollection<LastKeyState, LastKeyStateEntity>());

            var database = _databaseCoreManager.DatabaseCore;

            return new TreeValuesCache(database, snapshot.Object, _journalService, _alertScheduleProvider,
                new HSMServer.Core.ChangeSets.ChangeSetJournal(database));
        }

        private async Task<List<BaseValue>> ReadHistoryAsync(Guid sensorId)
        {
            var values = new List<BaseValue>();

            await foreach (var bytes in _databaseCoreManager.DatabaseCore.GetSensorValues(sensorId, DateTime.MinValue, DateTime.MaxValue))
                values.Add(_formatter.Deserialize(bytes));

            return values.OrderBy(v => v.Time).ToList();
        }
    }
}
