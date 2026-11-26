using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.DatabaseEntities.SnapshotEntity;
using HSMDatabase.AccessManager.Formatters;
using HSMSensorDataObjects.SensorValueRequests;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Managers;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Core.Tests.TreeValuesCacheTests.Fixture;
using HSMServer.Core.TreeStateSnapshot;
using HSMServer.Core.TreeStateSnapshot.States;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.TreeValuesCacheTests
{
    // A folded run of equal values ends at the storage-week boundary (Monday 00:00 UTC):
    // a value is folded into the stored last value only when both fall in the same week,
    // so a run never rewrites a row in a week that has already ended. The equal value that
    // arrives in the next week starts a new row there, exactly like a value that differs.
    public class AggregateWeekBoundaryTests : MonitoringCoreTestsBase<TreeValuesCacheFixture>
    {
        private static readonly MemoryPackFormatter _formatter = new();

        private static readonly Guid AccessKey = Guid.Parse(TestProductsManager.TestProductKey.Id);
        private static readonly Guid ProductId = TestProductsManager.ProductId;

        // A whole week in the past: every value below is old enough to be ordinary history.
        private static readonly DateTime Monday = StorageWeek.GetStart(DateTime.UtcNow).AddDays(-7);

        private FailingDatabaseCore _database;


        public AggregateWeekBoundaryTests(TreeValuesCacheFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture)
        {
        }


        protected override IDatabaseCore WrapDatabase(IDatabaseCore inner) => _database = new FailingDatabaseCore(inner, _ => false);


        [Fact]
        public async Task EqualValuesAcrossTheWeekBoundary_StartANewRowInTheNewWeek_AndTheOldWeekIsNotWrittenAgain()
        {
            var sensor = await CreateAggregatingSensorAsync("week_boundary/cross", Monday.AddMinutes(-3));

            await AddAsync(sensor, Monday.AddMinutes(-2), "A");
            await AddAsync(sensor, Monday.AddMinutes(-1), "A");

            using var writes = new WriteRecorder(_database, sensor.Id);

            await AddAsync(sensor, Monday.AddMinutes(1), "A");
            await AddAsync(sensor, Monday.AddMinutes(2), "A");

            // Every write after the boundary went to the new week: the old week's database
            // received nothing.
            Assert.NotEmpty(writes.All);
            Assert.All(writes.All, w => Assert.True(w.Time >= Monday, $"a write landed in the old week at {w.Time:O}"));

            // The first value of the new week is a new row; the next equal one folds into it.
            Assert.Equal(new Write(Monday.AddMinutes(1), Monday.AddMinutes(1), 1), writes.All[0]);
            Assert.Equal(new Write(Monday.AddMinutes(1), Monday.AddMinutes(2), 2), writes.All[^1]);

            var rows = await ReadHistoryAsync(sensor.Id);

            Assert.Equal(2, rows.Count);
            AssertRun(rows[0], Monday.AddMinutes(-3), Monday.AddMinutes(-1), 3);
            AssertRun(rows[1], Monday.AddMinutes(1), Monday.AddMinutes(2), 2);

            Assert.Equal(Monday.AddMinutes(1), sensor.LastValue.Time);
            Assert.Equal(Monday.AddMinutes(2), sensor.LastUpdate);
        }

        [Fact]
        public async Task EqualValuesInsideOneWeek_StillFoldIntoOneRow()
        {
            var start = Monday.AddDays(2);
            var sensor = await CreateAggregatingSensorAsync("week_boundary/inside", start);

            using var writes = new WriteRecorder(_database, sensor.Id);

            await AddAsync(sensor, start.AddHours(1), "A");
            await AddAsync(sensor, start.AddHours(30), "A");

            Assert.All(writes.All, w => Assert.Equal(start, w.Time));

            var row = Assert.Single(await ReadHistoryAsync(sensor.Id));
            AssertRun(row, start, start.AddHours(30), 3);
        }

        [Fact]
        public async Task RunStoredInAnEarlierWeek_IsNotRewrittenByTheFirstEqualValueAfterARestart()
        {
            // The row an upgraded server finds at start: a run that began in a week that
            // has ended, and that the previous server kept rewriting there.
            var start = Monday.AddDays(-3);
            var sensor = await CreateAggregatingSensorAsync("week_boundary/restart", start);

            await AddAsync(sensor, Monday.AddDays(-2), "A");
            await AddAsync(sensor, Monday.AddDays(-1), "A");

            var recorded = new FailingDatabaseCore(_databaseCoreManager.DatabaseCore, _ => false);
            await using var restarted = BuildCacheOver(recorded);
            using var writes = new WriteRecorder(recorded, sensor.Id);

            var result = await restarted.AddSensorValueAsync(AccessKey, ProductId, Value(sensor.Path, Monday.AddHours(1), "A"));
            Assert.True(result.IsOk, result.Error);

            var reloaded = restarted.GetSensor(sensor.Id);
            Assert.True(reloaded.AggregateValues);

            Assert.Equal([new Write(Monday.AddHours(1), Monday.AddHours(1), 1)], writes.All);

            var rows = await ReadHistoryAsync(sensor.Id);

            Assert.Equal(2, rows.Count);
            AssertRun(rows[0], start, Monday.AddDays(-1), 3);
            AssertRun(rows[1], Monday.AddHours(1), Monday.AddHours(1), 1);
        }

        [Fact]
        public async Task OlderEqualValueFromTheEndedWeek_IsNotFoldedIntoTheNewWeeksRun()
        {
            // The out-of-order fold: an older equal value used to fold into the cached newest
            // value whatever its time. From the week before the newest row's week it is a late
            // value of its own week: it takes the out-of-order write there, and the newest row
            // keeps its run.
            var sensor = await CreateAggregatingSensorAsync("week_boundary/late", Monday.AddMinutes(-3));

            await AddAsync(sensor, Monday.AddMinutes(-2), "A");
            await AddAsync(sensor, Monday.AddMinutes(1), "A");

            using var writes = new WriteRecorder(_database, sensor.Id);

            await AddAsync(sensor, Monday.AddSeconds(-30), "A");

            Assert.Equal([new Write(Monday.AddSeconds(-30), Monday.AddSeconds(-30), 1)], writes.All);
            Assert.Equal(1, Volatile.Read(ref sensor.OutOfOrderValuesStored));

            var rows = await ReadHistoryAsync(sensor.Id);

            Assert.Equal(3, rows.Count);
            AssertRun(rows[0], Monday.AddMinutes(-3), Monday.AddMinutes(-2), 2);
            AssertRun(rows[1], Monday.AddSeconds(-30), Monday.AddSeconds(-30), 1);
            AssertRun(rows[2], Monday.AddMinutes(1), Monday.AddMinutes(1), 1);

            Assert.Equal(Monday.AddMinutes(1), sensor.LastUpdate);
        }

        [Fact]
        public async Task OlderEqualValueFromTheSameWeek_StillFoldsIntoTheNewestRow()
        {
            var start = Monday.AddDays(3);
            var sensor = await CreateAggregatingSensorAsync("week_boundary/late_same_week", start);

            using var writes = new WriteRecorder(_database, sensor.Id);

            await AddAsync(sensor, start.AddMinutes(-1), "A");

            Assert.All(writes.All, w => Assert.Equal(start, w.Time));
            Assert.Equal(0, Volatile.Read(ref sensor.OutOfOrderValuesStored));

            var row = Assert.Single(await ReadHistoryAsync(sensor.Id));
            Assert.Equal(start, row.Time);
            Assert.Equal(2, row.AggregatedValuesCount);
        }

        [Fact]
        public async Task ValueThatStartsTheNewWeeksRun_RaisesNoAlert_LikeAFoldedValue()
        {
            // A "value is changed" alert compares a value with the sensor's last value, before
            // the value is stored; the first equal value of a new week compares equal, the same
            // as a folded one, so neither fires. A different value proves the alert is live.
            var sensor = await CreateAggregatingSensorAsync("week_boundary/alert", Monday.AddMinutes(-3));

            var update = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                Initiator = InitiatorInfo.AsSystemForce("tests"),
                Policies =
                [
                    new PolicyUpdate
                    {
                        Id = Guid.NewGuid(),
                        Template = "changed",
                        Destination = new PolicyDestinationUpdate(),
                        Conditions =
                        [
                            new PolicyConditionUpdate(PolicyOperation.IsChanged, PolicyProperty.Value,
                                new TargetValue(TargetType.LastValue, sensor.Id.ToString())),
                        ],
                    },
                ],
            });
            Assert.True(update.IsOk, update.Error);

            var alerts = new ConcurrentQueue<AlertMessage>();
            void Record(AlertMessage message)
            {
                if (message.SensorId == sensor.Id)
                    alerts.Enqueue(message);
            }

            _valuesCache.NewAlertMessageEvent += Record;
            try
            {
                await AddAsync(sensor, Monday.AddMinutes(-1), "A"); // folded
                AssertNoAlert(sensor, alerts);

                await AddAsync(sensor, Monday.AddMinutes(1), "A"); // starts the new week's row
                AssertNoAlert(sensor, alerts);

                Assert.Equal(Monday.AddMinutes(1), sensor.LastValue.Time);
                Assert.Equal(Monday.AddMinutes(1), sensor.LastUpdate);
                Assert.Equal(SensorStatus.Ok, sensor.Status?.Status);

                await AddAsync(sensor, Monday.AddMinutes(2), "A"); // folded into it
                AssertNoAlert(sensor, alerts);

                await AddAsync(sensor, Monday.AddMinutes(3), "B");
                Assert.NotEmpty(sensor.Policies.NotificationResult);
            }
            finally
            {
                _valuesCache.NewAlertMessageEvent -= Record;
            }

            Assert.Equal(3, (await ReadHistoryAsync(sensor.Id)).Count);
        }

        [Fact]
        public void WeekOfAValue_IsTheUtcWeekOfItsKey_WhateverTheKindItWasGivenIn()
        {
            // BaseValue.Time stores UTC whatever Kind it is set with, and the database key is
            // its UTC ticks; the week comparison reads those ticks only.
            var beforeLocal = Monday.AddMinutes(-1).ToLocalTime();
            var afterLocal = Monday.AddMinutes(1).ToLocalTime();

            var before = new StringValue { Time = beforeLocal, Value = "A" };
            var after = new StringValue { Time = afterLocal, Value = "A" };

            Assert.Equal(Monday.AddMinutes(-1).Ticks, before.Time.Ticks);
            Assert.False(StorageWeek.IsSame(before.Time, after.Time));
            Assert.True(StorageWeek.IsSame(after.Time, new DateTime(Monday.AddDays(7).Ticks - 1, DateTimeKind.Unspecified)));
        }


        private async Task<BaseSensorModel> CreateAggregatingSensorAsync(string path, DateTime firstTime)
        {
            var result = await _valuesCache.AddSensorValueAsync(AccessKey, ProductId, Value(path, firstTime, "A"));
            Assert.True(result.IsOk, result.Error);

            Assert.True(_valuesCache.TryGetSensorByPath(ProductId, path, out var sensor));

            var update = await _valuesCache.UpdateSensorAsync(new SensorUpdate { Id = sensor.Id, AggregateValues = true, Initiator = InitiatorInfo.System });
            Assert.True(update.IsOk, update.Error);

            return sensor;
        }

        private async Task AddAsync(BaseSensorModel sensor, DateTime time, string value)
        {
            var result = await _valuesCache.AddSensorValueAsync(AccessKey, ProductId, Value(sensor.Path, time, value));
            Assert.True(result.IsOk, result.Error);
        }

        private static StringSensorValue Value(string path, DateTime time, string value) => new()
        {
            Path = path,
            Time = time,
            Value = value,
            Status = HSMSensorDataObjects.SensorStatus.Ok,
        };

        private static void AssertRun(BaseValue row, DateTime time, DateTime lastUpdate, long count)
        {
            Assert.Equal(time, row.Time);
            Assert.Equal(lastUpdate, row.LastUpdateTime);
            Assert.Equal(count, row.AggregatedValuesCount);
        }

        private static void AssertNoAlert(BaseSensorModel sensor, ConcurrentQueue<AlertMessage> alerts)
        {
            Assert.Empty(sensor.Policies.NotificationResult);
            Assert.Empty(sensor.Policies.PolicyResult);
            Assert.Empty(alerts);
        }

        private async Task<List<BaseValue>> ReadHistoryAsync(Guid sensorId)
        {
            var values = new List<BaseValue>();

            await foreach (var bytes in _databaseCoreManager.DatabaseCore.GetSensorValues(sensorId, DateTime.MinValue, DateTime.MaxValue))
                values.Add(_formatter.Deserialize(bytes));

            return values.OrderBy(v => v.Time).ToList();
        }

        private TreeValuesCache BuildCacheOver(IDatabaseCore database)
        {
            var snapshot = new Mock<ITreeStateSnapshot>();
            snapshot.Setup(a => a.Keys).Returns(new StateCollection<LastKeyState, LastKeyStateEntity>());

            return new TreeValuesCache(database, snapshot.Object, _journalService, _alertScheduleProvider,
                new HSMServer.Core.ChangeSets.ChangeSetJournal(database));
        }


        private readonly record struct Write(DateTime Time, DateTime LastUpdateTime, long Count);

        // What reached the database for one sensor, copied at the call: the cache keeps
        // folding into the instance it wrote.
        private sealed class WriteRecorder : IDisposable
        {
            private readonly FailingDatabaseCore _database;
            private readonly ConcurrentQueue<Write> _writes = new();

            public WriteRecorder(FailingDatabaseCore database, Guid sensorId)
            {
                _database = database;
                _database.OnAddSensorValue = (id, value) =>
                {
                    if (id == sensorId)
                        _writes.Enqueue(new Write(value.Time, value.LastUpdateTime, value.AggregatedValuesCount));
                };
            }

            public List<Write> All => [.. _writes];

            public void Dispose() => _database.OnAddSensorValue = null;
        }
    }
}
