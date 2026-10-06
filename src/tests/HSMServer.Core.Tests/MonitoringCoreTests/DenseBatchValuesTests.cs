using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMSensorDataObjects.SensorValueRequests;
using HSMCommon.Model;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Extensions;
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


        [Fact]
        public async Task BarSensor_LatePartialSamePeriod_MergesInMemory_NoDuplicatedRows()
        {
            // Bars merge same-period partials in memory by OpenTime; the DB row
            // for a period appears only when the period closes. A partial sent
            // after a NEWER partial of the same period must NOT be persisted as
            // its own row (#1441 round-2): a bar row's DB key is its SEND time,
            // so a direct write would duplicate the period.
            var path = "denseBatch/barPeriod";
            var periodOpen = DateTime.UtcNow.AddMinutes(-5);
            var periodClose = periodOpen.AddSeconds(5);

            var batch = new SensorValueBase[]
            {
                // Second partial (later send time, same period) arrives FIRST.
                new DoubleBarSensorValue
                {
                    Path = path, Time = periodOpen.AddSeconds(3), OpenTime = periodOpen, CloseTime = periodClose,
                    Min = 0, Max = 10, Mean = 5, Count = 2, Status = HSMSensorDataObjects.SensorStatus.Ok,
                },
                // First partial (earlier send time, same period) arrives SECOND — out of send order.
                new DoubleBarSensorValue
                {
                    Path = path, Time = periodOpen.AddSeconds(1), OpenTime = periodOpen, CloseTime = periodClose,
                    Min = 1, Max = 9, Mean = 4, Count = 1, Status = HSMSensorDataObjects.SensorStatus.Ok,
                },
            };

            Assert.Empty((await _valuesCache.AddSensorValuesAsync(_fixture.AccessKeyAId, _fixture.ProductAId, batch)));

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, path, out var sensor));

            // Pre-existing bar bootstrap (unchanged by #1441): the FIRST value of
            // a sensor makes IsNewBar promote the partial into _prevValue, so one
            // bootstrap row for it exists immediately. The LATE partial must NOT
            // have added a row of its own — it merged in memory.
            var stored = await ReadStoredWindowAsync(sensor.Id, periodOpen.AddMinutes(-1), periodClose.AddMinutes(2));
            Assert.Single(stored);
            Assert.Equal(0, Volatile.Read(ref sensor.OutOfOrderValuesStored));
            Assert.Equal(0, Volatile.Read(ref sensor.SameTickValuesSuperseded));

            // The NEXT period closes the first one: exactly one row must appear
            // for it — the merged in-memory bar, not a per-partial duplicate.
            var nextOpen = periodClose;
            var nextClose = nextOpen.AddSeconds(5);
            var next = new SensorValueBase[]
            {
                new DoubleBarSensorValue
                {
                    Path = path, Time = nextOpen.AddSeconds(2), OpenTime = nextOpen, CloseTime = nextClose,
                    Min = 2, Max = 8, Mean = 5, Count = 1, Status = HSMSensorDataObjects.SensorStatus.Ok,
                },
            };
            Assert.Empty((await _valuesCache.AddSensorValuesAsync(_fixture.AccessKeyAId, _fixture.ProductAId, next)));

            stored = await ReadStoredWindowAsync(sensor.Id, periodOpen.AddMinutes(-1), nextClose.AddMinutes(2));

            // Exactly the pre-#1441 shape: the bootstrap row plus ONE row for the
            // closed period — no per-partial duplicate (a direct write of the late
            // partial would be a third row under its own send time).
            Assert.Equal(2, stored.Count);
            Assert.Equal(0, Volatile.Read(ref sensor.OutOfOrderValuesStored));
            Assert.Equal(0, Volatile.Read(ref sensor.SameTickValuesSuperseded));
        }

        [Fact]
        public async Task AggregateSensor_OlderEqualContentFolds_OlderDistinctContentPersistsDirectly()
        {
            // Pre-#1441 semantics restored (#1441 round-2): with AggregateValues
            // on, an OLDER value with EQUAL content folds into the cached newest
            // (no row of its own); only a non-foldable older value takes the
            // out-of-order direct write.
            var path = "denseBatch/aggregate";
            var baseTime = DateTime.UtcNow.AddMinutes(-10);

            var seed = new SensorValueBase[]
            {
                new DoubleSensorValue { Path = path, Time = baseTime, Value = 42, Status = HSMSensorDataObjects.SensorStatus.Ok },
            };
            Assert.Empty((await _valuesCache.AddSensorValuesAsync(_fixture.AccessKeyAId, _fixture.ProductAId, seed)));

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, path, out var sensor));
            await _valuesCache.UpdateSensorAsync(new SensorUpdate { Id = sensor.Id, AggregateValues = true, Initiator = InitiatorInfo.System });

            var before = await ReadStoredWindowAsync(sensor.Id, baseTime.AddMinutes(-1), baseTime.AddMinutes(1));

            // Older, EQUAL content: folds, no new row, no out-of-order counter.
            var olderEqual = new SensorValueBase[]
            {
                new DoubleSensorValue { Path = path, Time = baseTime.AddMinutes(-1), Value = 42, Status = HSMSensorDataObjects.SensorStatus.Ok },
            };
            Assert.Empty((await _valuesCache.AddSensorValuesAsync(_fixture.AccessKeyAId, _fixture.ProductAId, olderEqual)));

            var afterFold = await ReadStoredWindowAsync(sensor.Id, baseTime.AddMinutes(-5), baseTime.AddMinutes(1));
            Assert.Equal(before.Count, afterFold.Count);
            Assert.Equal(0, Volatile.Read(ref sensor.OutOfOrderValuesStored));

            // Older, DIFFERENT content: persisted directly as out-of-order.
            var olderDistinct = new SensorValueBase[]
            {
                new DoubleSensorValue { Path = path, Time = baseTime.AddMinutes(-2), Value = 7, Status = HSMSensorDataObjects.SensorStatus.Ok },
            };
            Assert.Empty((await _valuesCache.AddSensorValuesAsync(_fixture.AccessKeyAId, _fixture.ProductAId, olderDistinct)));

            var afterDirect = await ReadStoredWindowAsync(sensor.Id, baseTime.AddMinutes(-5), baseTime.AddMinutes(1));
            Assert.Equal(before.Count + 1, afterDirect.Count);
            Assert.Equal(1, Volatile.Read(ref sensor.OutOfOrderValuesStored));
        }

        [Fact]
        public async Task OutOfOrderValue_OlderThanKeepHistoryWindow_IsNotStored_AndCounted()
        {
            // Retention floor (#1441): with KeepHistory configured, an
            // out-of-order value older than the retention window is not
            // written (the retention pass would purge it anyway) — counted as
            // OutOfRetentionValues. Within the window, out-of-order values
            // keep being stored.
            var path = "denseBatch/retentionKeep";
            var newest = DateTime.UtcNow.AddHours(-1);

            var seed = new SensorValueBase[]
            {
                new DoubleSensorValue { Path = path, Time = newest, Value = 1, Status = HSMSensorDataObjects.SensorStatus.Ok },
            };
            Assert.Empty((await _valuesCache.AddSensorValuesAsync(_fixture.AccessKeyAId, _fixture.ProductAId, seed)));

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, path, out var sensor));
            await _valuesCache.UpdateSensorAsync(new SensorUpdate { Id = sensor.Id, KeepHistory = new TimeIntervalModel(TimeSpan.FromDays(1).Ticks), Initiator = InitiatorInfo.System });

            // In-window out-of-order (older than the newest, newer than the floor): stored.
            var inWindow = new SensorValueBase[]
            {
                new DoubleSensorValue { Path = path, Time = newest.AddHours(-2), Value = 2, Status = HSMSensorDataObjects.SensorStatus.Ok },
            };
            Assert.Empty((await _valuesCache.AddSensorValuesAsync(_fixture.AccessKeyAId, _fixture.ProductAId, inWindow)));
            Assert.Equal(1, Volatile.Read(ref sensor.OutOfOrderValuesStored));
            Assert.Equal(0, Volatile.Read(ref sensor.OutOfRetentionValues));

            // Out-of-window (older than UtcNow - 1 day): not stored, counted.
            var outOfWindow = new SensorValueBase[]
            {
                new DoubleSensorValue { Path = path, Time = DateTime.UtcNow.AddDays(-3), Value = 3, Status = HSMSensorDataObjects.SensorStatus.Ok },
            };
            Assert.Empty((await _valuesCache.AddSensorValuesAsync(_fixture.AccessKeyAId, _fixture.ProductAId, outOfWindow)));
            Assert.Equal(1, Volatile.Read(ref sensor.OutOfRetentionValues));

            var stored = await ReadStoredWindowAsync(sensor.Id, DateTime.UtcNow.AddDays(-5), newest.AddMinutes(1));
            Assert.Equal(2, stored.Count); // seed + the in-window value; the out-of-window one is not stored
        }

        [Fact]
        public async Task OutOfOrderValue_OlderThanOldestRow_ButWithinKeepHistory_IsStored()
        {
            // The restart shape (#1441 round-3): the history load seeds
            // Storage.From with the OLDEST STORED ROW, which is not a retention
            // boundary — a backfill older than the first row but inside the
            // KeepHistory window must be stored, or the fix would depend on the
            // server's restart history.
            var path = "denseBatch/retentionRestart";
            var firstRow = DateTime.UtcNow.AddHours(-2);

            var seed = new SensorValueBase[]
            {
                new DoubleSensorValue { Path = path, Time = firstRow, Value = 1, Status = HSMSensorDataObjects.SensorStatus.Ok },
            };
            Assert.Empty((await _valuesCache.AddSensorValuesAsync(_fixture.AccessKeyAId, _fixture.ProductAId, seed)));

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, path, out var sensor));
            await _valuesCache.UpdateSensorAsync(new SensorUpdate { Id = sensor.Id, KeepHistory = new TimeIntervalModel(TimeSpan.FromDays(7).Ticks), Initiator = InitiatorInfo.System });

            // What a restart does: the history load cuts From to the oldest row.
            sensor.Cut(firstRow);

            var backfill = new SensorValueBase[]
            {
                new DoubleSensorValue { Path = path, Time = firstRow.AddHours(-1), Value = 2, Status = HSMSensorDataObjects.SensorStatus.Ok },
            };
            Assert.Empty((await _valuesCache.AddSensorValuesAsync(_fixture.AccessKeyAId, _fixture.ProductAId, backfill)));

            Assert.Equal(1, Volatile.Read(ref sensor.OutOfOrderValuesStored));
            Assert.Equal(0, Volatile.Read(ref sensor.OutOfRetentionValues));

            var stored = await ReadStoredWindowAsync(sensor.Id, firstRow.AddHours(-2), firstRow.AddMinutes(1));
            Assert.Equal(2, stored.Count); // the seeded row AND the backfill older than it
        }

        [Fact]
        public async Task OutOfOrderRow_BelowStorageFrom_IsRemovedByFullClear()
        {
            // #1441 round-5: the direct out-of-order write can land a row
            // OLDER than Storage.From (which a restart seeds with the oldest
            // stored row). ClearSensorHistory deletes [sensor.From, to] and
            // the retention pass starts at From too — unless From is widened
            // down to the new oldest row, a full operator clear leaves that
            // row in the database (still visible in history reads) until a
            // restart.
            var path = "denseBatch/belowFromClear";
            var firstRow = DateTime.UtcNow.AddHours(-2);

            var seed = new SensorValueBase[]
            {
                new DoubleSensorValue { Path = path, Time = firstRow, Value = 1, Status = HSMSensorDataObjects.SensorStatus.Ok },
            };
            Assert.Empty((await _valuesCache.AddSensorValuesAsync(_fixture.AccessKeyAId, _fixture.ProductAId, seed)));

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, path, out var sensor));

            // What a restart does: the history load cuts From to the oldest row.
            sensor.Cut(firstRow);
            Assert.Equal(firstRow, sensor.From);

            // A shuffled batch backfills BELOW From: stored directly (#1441)...
            var backfillTime = firstRow.AddMinutes(-30);
            var backfill = new SensorValueBase[]
            {
                new DoubleSensorValue { Path = path, Time = backfillTime, Value = 2, Status = HSMSensorDataObjects.SensorStatus.Ok },
            };
            Assert.Empty((await _valuesCache.AddSensorValuesAsync(_fixture.AccessKeyAId, _fixture.ProductAId, backfill)));
            Assert.Equal(1, Volatile.Read(ref sensor.OutOfOrderValuesStored));

            // ...and From now tracks the new oldest row (the fix under test):
            // MinValue itself never widens, but any From above the row does.
            Assert.Equal(backfillTime, sensor.From);

            var beforeClear = await ReadStoredWindowAsync(sensor.Id, backfillTime.AddMinutes(-5), firstRow.AddMinutes(1));
            Assert.Equal(2, beforeClear.Count);

            // A full UI clear must remove BOTH rows — the backfilled one
            // below the old From included — and must not get refused.
            await _valuesCache.ClearSensorHistoryAsync(new ClearHistoryRequest(sensor.Id));
            Assert.NotNull(sensor.HistoryClearedTo); // the clear ran, not refused

            var afterClear = await ReadStoredWindowAsync(sensor.Id, DateTime.MinValue, DateTime.MaxValue);
            Assert.Empty(afterClear);
        }

        [Fact]
        public async Task OutOfOrderFileValue_IsStoredCompressed()
        {
            // #1441 round-5: the out-of-order direct write bypasses
            // Storage.AddValue, where FileValuesStorage compresses incoming
            // content — the persisted copy must get the same write-side
            // transform (PrepareForPersist), or late file values would sit
            // in the database raw, larger on disk than the same content
            // through the storage's rules. Readers are agnostic:
            // DecompressContent passes through when Value.Length ==
            // OriginalSize and decompresses otherwise.
            var path = "denseBatch/fileOutOfOrderCompressed";
            var baseTime = DateTime.UtcNow.AddMinutes(-10);
            var content = Enumerable.Repeat((byte)'A', 50_000).ToArray();

            var batch = new SensorValueBase[]
            {
                // In-order first: becomes the cached newest.
                new FileSensorValue { Path = path, Time = baseTime, Value = [.. content], Name = "inOrder", Extension = "txt", Status = HSMSensorDataObjects.SensorStatus.Ok },
                // The late (older) sibling: the #1441 direct write.
                new FileSensorValue { Path = path, Time = baseTime.AddMinutes(-5), Value = [.. content], Name = "outOfOrder", Extension = "txt", Status = HSMSensorDataObjects.SensorStatus.Ok },
            };
            Assert.Empty((await _valuesCache.AddSensorValuesAsync(_fixture.AccessKeyAId, _fixture.ProductAId, batch)));

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, path, out var sensor));
            Assert.Equal(1, Volatile.Read(ref sensor.OutOfOrderValuesStored));

            var stored = await ReadStoredWindowAsync(sensor.Id, baseTime.AddHours(-1), baseTime.AddMinutes(1));
            Assert.Equal(2, stored.Count);

            var late = Assert.IsType<FileValue>(stored.Single(v => v is FileValue file && file.Name == "outOfOrder"));

            // Compressed on disk, and the decompress roundtrip returns the content.
            Assert.True(late.Value.Length < late.OriginalSize,
                $"the persisted out-of-order file value must be compressed: Value.Length={late.Value.Length}, OriginalSize={late.OriginalSize}");
            Assert.Equal(content, late.DecompressContent().Value);
        }

        [Fact]
        public async Task OutOfOrderValue_IntoClearedWindow_IsNotStored()
        {
            // The other floor half: after an explicit history clear, a late
            // value inside the cleared window must not re-fill it.
            var path = "denseBatch/retentionCleared";
            var newest = DateTime.UtcNow.AddMinutes(-30);

            var seed = new SensorValueBase[]
            {
                // Oldest first (monotonic): the seed itself must not register as out-of-order.
                new DoubleSensorValue { Path = path, Time = newest.AddMinutes(-20), Value = 2, Status = HSMSensorDataObjects.SensorStatus.Ok },
                new DoubleSensorValue { Path = path, Time = newest, Value = 1, Status = HSMSensorDataObjects.SensorStatus.Ok },
            };
            Assert.Empty((await _valuesCache.AddSensorValuesAsync(_fixture.AccessKeyAId, _fixture.ProductAId, seed)));

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, path, out var sensor));
            Assert.Equal(0, Volatile.Read(ref sensor.OutOfOrderValuesStored));

            var clearedTo = newest.AddMinutes(-10); // erase the older value's window edge
            await _valuesCache.ClearSensorHistoryAsync(new ClearHistoryRequest(sensor.Id, clearedTo));
            Assert.NotNull(sensor.HistoryClearedTo); // probe: the clear actually ran through the queue

            var lateIntoCleared = new SensorValueBase[]
            {
                new DoubleSensorValue { Path = path, Time = newest.AddMinutes(-25), Value = 3, Status = HSMSensorDataObjects.SensorStatus.Ok },
            };
            Assert.Empty((await _valuesCache.AddSensorValuesAsync(_fixture.AccessKeyAId, _fixture.ProductAId, lateIntoCleared)));

            Assert.Equal(0, Volatile.Read(ref sensor.OutOfOrderValuesStored));
            Assert.Equal(1, Volatile.Read(ref sensor.OutOfRetentionValues));
        }


        [Fact]
        public async Task FullUiClear_LaterValuesAfterClearMoment_AreStored()
        {
            // The UI "Clear history" sends the default ClearHistoryRequest
            // (To = DateTime.MaxValue). Stamping that verbatim would floor at
            // 9999-12-31 and drop every later out-of-order value until restart
            // (#1441 round-4). The floor must be the clear MOMENT: values
            // stamped after it are stored; a replay of pre-clear stamps is not.
            var path = "denseBatch/fullClear";
            var seedTime = DateTime.UtcNow.AddMinutes(-30);

            var seed = new SensorValueBase[]
            {
                new DoubleSensorValue { Path = path, Time = seedTime, Value = 1, Status = HSMSensorDataObjects.SensorStatus.Ok },
            };
            Assert.Empty((await _valuesCache.AddSensorValuesAsync(_fixture.AccessKeyAId, _fixture.ProductAId, seed)));

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, path, out var sensor));

            var clearMoment = DateTime.UtcNow;
            await _valuesCache.ClearSensorHistoryAsync(new ClearHistoryRequest(sensor.Id)); // UI shape: To defaults to MaxValue
            Assert.NotNull(sensor.HistoryClearedTo);
            Assert.True(sensor.HistoryClearedTo.Value <= clearMoment.AddSeconds(1), "the floor must clamp to the clear moment, not MaxValue");

            // Fresh data after the clear: becomes the cached newest.
            var newest = new SensorValueBase[]
            {
                new DoubleSensorValue { Path = path, Time = clearMoment.AddSeconds(10), Value = 2, Status = HSMSensorDataObjects.SensorStatus.Ok },
            };
            Assert.Empty((await _valuesCache.AddSensorValuesAsync(_fixture.AccessKeyAId, _fixture.ProductAId, newest)));

            // Out-of-order but stamped AFTER the clear moment: stored.
            var afterClearBackfill = new SensorValueBase[]
            {
                new DoubleSensorValue { Path = path, Time = clearMoment.AddSeconds(5), Value = 3, Status = HSMSensorDataObjects.SensorStatus.Ok },
            };
            Assert.Empty((await _valuesCache.AddSensorValuesAsync(_fixture.AccessKeyAId, _fixture.ProductAId, afterClearBackfill)));
            Assert.Equal(1, Volatile.Read(ref sensor.OutOfOrderValuesStored));
            Assert.Equal(0, Volatile.Read(ref sensor.OutOfRetentionValues));

            // A replay of PRE-clear stamps: refused, counted.
            var preClearReplay = new SensorValueBase[]
            {
                new DoubleSensorValue { Path = path, Time = seedTime.AddMinutes(5), Value = 4, Status = HSMSensorDataObjects.SensorStatus.Ok },
            };
            Assert.Empty((await _valuesCache.AddSensorValuesAsync(_fixture.AccessKeyAId, _fixture.ProductAId, preClearReplay)));
            Assert.Equal(1, Volatile.Read(ref sensor.OutOfRetentionValues));
        }

        [Fact]
        public async Task RetentionPassClear_DoesNotLowerAnOperatorClearFloor()
        {
            // The automatic KeepHistory pass runs through the same
            // ClearSensorHistory with its own cutoff; it must never LOWER a
            // floor a later operator clear raised (#1441 round-4 P3).
            //
            // Round-5 note on actually reaching the guard: the operator
            // clear ends with sensor.Cut(to), pinning From at T2, so a
            // retention cutoff T1 < T2 would take the from > to early
            // return and never evaluate the max-update. In a single session
            // nothing lowers From below the floor honestly (values below
            // the floor are refused; a restart cuts From to the oldest row,
            // which the operator clear just made newer than T2) — the guard
            // is reachable only in the From-reverted shape the restart race
            // documented at LoadHistoryUnderLock can leave behind. That
            // shape is simulated below with this file's established restart
            // idiom (sensor.Cut), so the second clear genuinely runs through
            // the floor guard instead of the early return.
            var path = "denseBatch/floorMonotone";
            var newest = DateTime.UtcNow.AddMinutes(-5);

            var seed = new SensorValueBase[]
            {
                new DoubleSensorValue { Path = path, Time = newest.AddMinutes(-30), Value = 1, Status = HSMSensorDataObjects.SensorStatus.Ok },
                new DoubleSensorValue { Path = path, Time = newest, Value = 2, Status = HSMSensorDataObjects.SensorStatus.Ok },
            };
            Assert.Empty((await _valuesCache.AddSensorValuesAsync(_fixture.AccessKeyAId, _fixture.ProductAId, seed)));

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, path, out var sensor));

            // Operator clears up to T2 (the later bound): the floor is raised...
            var t2 = newest.AddMinutes(-10);
            await _valuesCache.ClearSensorHistoryAsync(new ClearHistoryRequest(sensor.Id, t2));
            Assert.Equal(t2, sensor.HistoryClearedTo);

            // ...and the clear's Cut leaves From AT T2. Revert From below T1
            // (the restart-race shape above) so the retention pass genuinely
            // reaches the max-update guard.
            var t1 = newest.AddMinutes(-25);
            sensor.Cut(t1.AddMinutes(-1));

            // The retention pass clears up to an EARLIER cutoff T1 < T2: it
            // runs (from <= to) and must NOT lower the operator's floor.
            await _valuesCache.ClearSensorHistoryAsync(new ClearHistoryRequest(sensor.Id, t1));

            Assert.Equal(t1, sensor.From); // the second clear RAN: its Cut moved From up to T1 (an early return would have left it below)
            Assert.Equal(t2, sensor.HistoryClearedTo); // the floor did not regress

            // A late value inside (T1, T2) — inside the operator's cleared
            // window — must still be refused despite the later T1 pass.
            var intoOperatorWindow = new SensorValueBase[]
            {
                new DoubleSensorValue { Path = path, Time = t2.AddMinutes(-5), Value = 3, Status = HSMSensorDataObjects.SensorStatus.Ok },
            };
            Assert.Empty((await _valuesCache.AddSensorValuesAsync(_fixture.AccessKeyAId, _fixture.ProductAId, intoOperatorWindow)));
            Assert.Equal(1, Volatile.Read(ref sensor.OutOfRetentionValues));
            Assert.Equal(0, Volatile.Read(ref sensor.OutOfOrderValuesStored));
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
