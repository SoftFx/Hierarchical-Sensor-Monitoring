using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMSensorDataObjects.SensorValueRequests;
using HSMCommon.Model;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Core.Tests.TreeValuesCacheTests.Fixture;
using Xunit;

namespace HSMServer.Core.Tests.MonitoringCoreTests
{
    // #1441 (TAM-1870): dense value bursts through the batch ingestion entry
    // (POST /api/Sensors/list -> AddSensorValuesAsync) silently lost most of
    // their points — a 200 OK, no dropped-value signal, only a fraction of the
    // batch queryable afterwards. These tests pin the opposite: EVERY accepted
    // batch value must end up stored and queryable through the same history
    // read the UI/management API use, whatever its timestamp density.
    //
    // The seam is deliberately the full real wiring of MonitoringCoreTestsBase
    // (real TreeValuesCache over a real LevelDB DatabaseCore) — the loss was
    // reproducible only through the write-through path, not through the
    // storage primitives in isolation.
    [Collection("Database collection")]
    // TemplateConcurrencyFixture is reused for its ProductAId/AccessKeyAId
    // wiring only — there is no template-concurrency angle in this suite.
    public class DenseBatchValuesTests : MonitoringCoreTestsBase<TemplateConcurrencyFixture>
    {
        private const int DenseCount = 1200;

        private readonly TemplateConcurrencyFixture _fixture;


        public DenseBatchValuesTests(TemplateConcurrencyFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture, addTestProduct: false)
        {
            _fixture = fixture;
        }


        [Fact]
        public async Task DenseBatch_OneSecondApart_EveryValueIsStoredAndQueryable()
        {
            var stored = await SendDenseBatchAndReadBackAsync("denseBatch/oneSecond", TimeSpan.FromSeconds(1));

            Assert.Equal(DenseCount, stored.Count);
        }

        [Fact]
        public async Task SmallDenseBatch_TenValuesOneSecondApart_AllStored()
        {
            // The minimal shape from the issue's repro plan: enough to fail a
            // coarse-truncation key, cheap to debug.
            var path = "denseBatch/tenValues";
            var baseTime = DateTime.UtcNow.AddMinutes(-5);

            var stored = await SendBatchAndReadBackAsync(path, baseTime, 10, TimeSpan.FromSeconds(1));

            Assert.Equal(10, stored.Count);
        }

        [Fact]
        public async Task SparseBatch_OneMinuteApart_EveryValueIsStoredAndQueryable()
        {
            // Control: the same volume at one-minute resolution was always
            // stored correctly — this pins that the dense assertions are not
            // just "the read window is wrong".
            var stored = await SendDenseBatchAndReadBackAsync("denseBatch/oneMinute", TimeSpan.FromMinutes(1));

            Assert.Equal(DenseCount, stored.Count);
        }

        [Fact]
        public async Task DenseBatch_ShuffledTimestamps_EveryValueIsStoredAndQueryable()
        {
            // Real collectors retry and backfill, so a batch can arrive with
            // non-monotonic timestamps. AddValueBase used to enqueue only
            // values newer than the cached last one — everything else fell on
            // the floor silently (200 OK, no dropped-value signal): a shuffled
            // 1200-value burst kept only the record maxima, a handful.
            var path = "denseBatch/shuffled";
            var baseTime = DateTime.UtcNow.AddDays(-2);
            var batch = BuildBatch(path, baseTime, DenseCount, TimeSpan.FromSeconds(1));
            Shuffle(batch);

            var response = await _valuesCache.AddSensorValuesAsync(_fixture.AccessKeyAId, _fixture.ProductAId, batch);
            Assert.Empty(response);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, path, out var sensor));

            var stored = await ReadStoredWindowAsync(sensor.Id, baseTime.AddMinutes(-1), baseTime.AddMinutes(21));

            Assert.Equal(DenseCount, stored.Count);

            // The unusual ingest shape is visible on the sensor, not silent (Rule #8):
            // every value that arrived older than the cached newest was persisted
            // directly — exactly the batch entries that are NOT running-time maxima.
            var cachedNewest = 0;
            DateTime? runningMax = null;
            foreach (var value in batch)
            {
                if (runningMax is null || value.Time >= runningMax)
                {
                    cachedNewest++;
                    runningMax = value.Time;
                }
            }

            Assert.Equal(DenseCount - cachedNewest, Volatile.Read(ref sensor.OutOfOrderValuesStored));
        }

        [Fact]
        public async Task DenseBatch_DuplicateTimestamps_LastValuePerTickIsKept_AndTheSupersedeIsCounted()
        {
            // Two values inside one timestamp (retry, batch rebuild, or a
            // collector clock with coarse resolution) collapse to the last one
            // in the database: the write key is (sensorId, ticks), a
            // compatibility-frozen format, so the second Put overwrites the
            // first row. That collapse cannot be avoided without a key-format
            // migration — but it must not be SILENT (#1441, Rule #8): every
            // supersede is counted on the sensor and Warn-logged at a bounded
            // rate (first occurrence, then every 1000th, with a running total).
            var path = "denseBatch/duplicates";
            var baseTime = DateTime.UtcNow.AddDays(-2);
            const int seconds = 10;
            const int perSecond = 2;

            var batch = Enumerable.Range(0, seconds)
                .SelectMany(second => Enumerable.Range(0, perSecond)
                    .Select(copy => new DoubleSensorValue
                    {
                        Path = path,
                        Time = baseTime.AddSeconds(second),
                        Value = second * perSecond + copy,
                        Status = HSMSensorDataObjects.SensorStatus.Ok,
                    }))
                .Cast<SensorValueBase>()
                .ToList();

            var response = await _valuesCache.AddSensorValuesAsync(_fixture.AccessKeyAId, _fixture.ProductAId, batch);
            Assert.Empty(response);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, path, out var sensor));

            var stored = await ReadStoredWindowAsync(sensor.Id, baseTime.AddMinutes(-1), baseTime.AddMinutes(1));

            // One row per distinct timestamp; within a tick the LAST value wins.
            Assert.Equal(seconds, stored.Count);
            Assert.All(stored.Where(v => v is DoubleValue), v => Assert.Equal(1, ((DoubleValue)v).Value % perSecond));

            // The collapse is visible, not silent: every superseded value counted.
            Assert.Equal(seconds * (perSecond - 1), Volatile.Read(ref sensor.SameTickValuesSuperseded));
        }


        [Fact]
        public async Task SingletonSecondValueInSameSecond_IsRejected_AndTheRejectionIsCounted()
        {
            // A sensor with the singleton option keeps at most one value per
            // second: a second value inside the same second is rejected by
            // design — but it used to vanish with no trace (Rule #8). The
            // rejection is now counted; deliberately not logged per value —
            // singleton rejections are steady state (N app instances feeding
            // one singleton sensor reject N-1 values per second forever) and
            // the counter is the visibility surface.
            var path = "denseBatch/singleton";
            var now = DateTime.UtcNow.AddMinutes(-1);
            var time = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond)); // whole second

            var first = new SensorValueBase[] { new BoolSensorValue { Path = path, Time = time, Value = true, Status = HSMSensorDataObjects.SensorStatus.Ok } };
            Assert.Empty((await _valuesCache.AddSensorValuesAsync(_fixture.AccessKeyAId, _fixture.ProductAId, first)));

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, path, out var sensor));
            await _valuesCache.UpdateSensorAsync(new SensorUpdate { Id = sensor.Id, IsSingleton = true, Initiator = InitiatorInfo.System });

            var second = new SensorValueBase[] { new BoolSensorValue { Path = path, Time = time.AddMilliseconds(500), Value = false, Status = HSMSensorDataObjects.SensorStatus.Ok } };
            Assert.Empty((await _valuesCache.AddSensorValuesAsync(_fixture.AccessKeyAId, _fixture.ProductAId, second)));

            var stored = await ReadStoredWindowAsync(sensor.Id, time.AddMinutes(-1), time.AddMinutes(1));

            Assert.Single(stored); // the singleton gate: one value per second
            Assert.Equal(1, Volatile.Read(ref sensor.RejectedValues));
        }


        [Fact]
        public async Task UiAddedValue_OlderThanCachedNewest_IsStoredDirectly_AndOutOfOrderCounted()
        {
            // The UI add-value entry (HomeController.UpdateSensorStatus ->
            // UpdateSensorValueAsync, ChangeLast = false) now routes through the
            // same PersistAddedValue tail as the batch API. The request has no
            // time field — BuildNewValue stamps DateTime.UtcNow on the product
            // queue thread — so the deterministic way to put a UI value OLDER
            // than the cached newest is a future-dated sibling: UtcNow is always
            // older than "an hour ahead".
            var path = "denseBatch/uiAddOutOfOrder";
            var futureTime = DateTime.UtcNow.AddHours(1);

            var seed = new SensorValueBase[] { new DoubleSensorValue { Path = path, Time = futureTime, Value = 123.45, Status = HSMSensorDataObjects.SensorStatus.Ok } };
            Assert.Empty((await _valuesCache.AddSensorValuesAsync(_fixture.AccessKeyAId, _fixture.ProductAId, seed)));

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, path, out var sensor));

            await AddUiValueAsync(sensor, "42");

            // The UI value landed in the database (the future-dated seed is
            // outside the window) and is queryable through the same history
            // read the UI uses.
            var stored = await ReadStoredWindowAsync(sensor.Id, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));

            var row = Assert.IsType<DoubleValue>(Assert.Single(stored));
            Assert.Equal(42, row.Value);
            Assert.Equal(1, Volatile.Read(ref sensor.OutOfOrderValuesStored));

            // The cache's newest value is untouched by the older sibling, and the
            // out-of-order shape must not count as a same-tick supersede even
            // though its before/after newest comparison trivially matches.
            Assert.Equal(futureTime, sensor.LastValue.Time);
            Assert.Equal(0, Volatile.Read(ref sensor.SameTickValuesSuperseded));
        }

        [Fact]
        public async Task UiAddedValues_AtDistinctTicks_BothStored_NoFalseSameTickSupersede()
        {
            // The exact same-tick pair of the duplicate-timestamps test cannot be
            // planted through UpdateSensorValueAsync: the request has no time
            // field and the queue thread's DateTime.UtcNow never coincides with a
            // pre-planted tick (the clock is precise, and the queue hop alone is
            // thousands of ticks). The shared detection branch itself is covered
            // by DenseBatch_DuplicateTimestamps_LastValuePerTickIsKept_AndTheSupersedeIsCounted
            // above; this pins the UI-path wiring around it — every accepted UI
            // add is persisted through the same tail, and the supersede
            // comparison (previous cached newest vs the value just added) does
            // not misfire on distinct ticks.
            var path = "denseBatch/uiAddDistinctTicks";
            var baseTime = DateTime.UtcNow.AddMinutes(-5);

            var seed = new SensorValueBase[] { new DoubleSensorValue { Path = path, Time = baseTime, Value = 0, Status = HSMSensorDataObjects.SensorStatus.Ok } };
            Assert.Empty((await _valuesCache.AddSensorValuesAsync(_fixture.AccessKeyAId, _fixture.ProductAId, seed)));

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, path, out var sensor));

            await AddUiValueAsync(sensor, "1");
            await AddUiValueAsync(sensor, "2");

            var stored = await ReadStoredWindowAsync(sensor.Id, baseTime.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));

            // Seed plus both UI rows; the last UI value owns the newest row
            // (the window read walks the database newest-to-oldest).
            Assert.Equal(3, stored.Count);
            Assert.Equal(2, Assert.IsType<DoubleValue>(stored[0]).Value);

            Assert.Equal(0, Volatile.Read(ref sensor.OutOfOrderValuesStored));
            Assert.Equal(0, Volatile.Read(ref sensor.SameTickValuesSuperseded));
        }


        private static void Shuffle<T>(IList<T> list)
        {
            var random = new Random(20260930); // deterministic: the loss must not depend on the shuffle
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }


        private async Task<List<BaseValue>> SendDenseBatchAndReadBackAsync(string path, TimeSpan spacing) =>
            await SendBatchAndReadBackAsync(path, DateTime.UtcNow.AddDays(-2), DenseCount, spacing);

        private async Task<List<BaseValue>> SendBatchAndReadBackAsync(string path, DateTime baseTime, int count, TimeSpan spacing)
        {
            var batch = BuildBatch(path, baseTime, count, spacing);

            var response = await _valuesCache.AddSensorValuesAsync(_fixture.AccessKeyAId, _fixture.ProductAId, batch);
            Assert.Empty(response); // premise: the whole batch was accepted — no per-value error

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, path, out var sensor));

            return await ReadStoredWindowAsync(sensor.Id, baseTime.AddMinutes(-1), baseTime.AddSeconds((count - 1) * spacing.TotalSeconds).AddMinutes(1));
        }

        private static List<SensorValueBase> BuildBatch(string path, DateTime baseTime, int count, TimeSpan spacing) =>
            Enumerable.Range(0, count)
                .Select(i => new DoubleSensorValue
                {
                    Path = path,
                    Time = baseTime.AddSeconds(i * spacing.TotalSeconds),
                    Value = i,
                    Status = HSMSensorDataObjects.SensorStatus.Ok,
                })
                .Cast<SensorValueBase>()
                .ToList();

        private async Task<List<BaseValue>> ReadStoredWindowAsync(Guid sensorId, DateTime from, DateTime to)
        {
            var stored = new List<BaseValue>();

            // The same paged read the history controllers use; negative count
            // = the full range, and pages walk the database newest-to-oldest
            // (MaxHistoryCount convention, GetValuesTo's reverse iterator).
            await foreach (var page in _valuesCache.GetSensorValuesPage(sensorId, from, to, -TreeValuesCache.MaxHistoryCount))
                stored.AddRange(page);

            return stored;
        }

        // A UI add exactly as HomeController.UpdateSensorStatus builds it: the
        // comment is what makes the request carry a value at all, and
        // ChangeLast = false selects the ADD branch (not the replace-last one).
        private async Task AddUiValueAsync(BaseSensorModel sensor, string value)
        {
            var result = await _valuesCache.UpdateSensorValueAsync(new UpdateSensorValueRequestModel(sensor.Id, sensor.Path)
            {
                Id = sensor.Id,
                Status = SensorStatus.Ok,
                Comment = "operator-added value",
                Value = value,
                ChangeLast = false,
            });

            Assert.True(result.IsOk);
        }
    }
}
