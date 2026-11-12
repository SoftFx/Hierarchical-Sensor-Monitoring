using HSMCommon.Model;
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
            await TestWait.UntilAsync(() => ReadHistoryAsync(moved.Id).Result.Count == 5, "every path's value was stored in the surviving sensor");
            Assert.Equal(t3, moved.LastValue.Time);
            Assert.False(_valuesCache.TryGetSensorByPath(_fixture.ProductB, $"{root}/before", out _));
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

        private async Task<List<BaseValue>> ReadHistoryAsync(Guid sensorId)
        {
            var values = new List<BaseValue>();

            await foreach (var bytes in _databaseCoreManager.DatabaseCore.GetSensorValues(sensorId, DateTime.MinValue, DateTime.MaxValue))
                values.Add(_formatter.Deserialize(bytes));

            return values.OrderBy(v => v.Time).ToList();
        }
    }
}
