using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMDatabase.AccessManager.Formatters;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Sensors.SensorModels;
using HSMServer.Core.Tests.Infrastructure;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.TreeValuesCacheTests
{
    /// <summary>
    /// Pins the Initialize() publication contract (#1296): _isInitialized must become observable only
    /// AFTER the history load has filled Storage, so a value arriving while the load is in flight
    /// waits instead of racing past the gate on an empty Storage. No fixture — these tests use a
    /// mocked IDatabaseCore, never LevelDB.
    /// </summary>
    public class SensorInitializationTests
    {
        private static readonly TimeSpan _waitTimeout = TimeSpan.FromSeconds(10);


        [Fact]
        [Trait("Category", "Initialization race")]
        public void TryAddValue_DuringHistoryLoad_WaitsForLoadedStorage()
        {
            var historyBytes = new MemoryPackFormatter().Serialize(
                new IntegerValue { Time = DateTime.UtcNow.AddMinutes(-5), Status = SensorStatus.Ok, Value = 1 });

            using var loadEntered = new ManualResetEventSlim(false);
            using var loadGate = new ManualResetEventSlim(false);

            var database = new Mock<IDatabaseCore>();
            database.Setup(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()))
                .Returns(() =>
                {
                    loadEntered.Set();
                    loadGate.Wait(_waitTimeout);
                    return historyBytes;
                });
            database.Setup(db => db.GetFirstValue(It.IsAny<Guid>())).Returns(historyBytes);

            var sensor = new IntegerSensorModel(BuildEntity(), database.Object, null);

            var init = Task.Run(sensor.Initialize);
            Assert.True(loadEntered.Wait(_waitTimeout), "history load never started");

            var newValue = new IntegerValue { Time = DateTime.UtcNow, Status = SensorStatus.Ok, Value = 2 };
            var added = false;
            using var addStarted = new ManualResetEventSlim(false);
            var add = Task.Run(() =>
            {
                addStarted.Set();
                added = sensor.TryAddValue(newValue);
            });

            Assert.True(addStarted.Wait(_waitTimeout), "TryAddValue task never started");
            // The load is still in flight (the gate is closed), so the writer must be parked on the
            // initialization lock. Pre-fix code latched the flag before reading the database, and
            // this Wait returned true immediately — TryAddValue completed against an empty Storage.
            Assert.False(add.Wait(TimeSpan.FromMilliseconds(200)),
                "TryAddValue completed while the history load was still in flight");

            loadGate.Set();
            Assert.True(Task.WaitAll(new[] { init, add }, _waitTimeout), "init/add did not finish");

            Assert.True(added);
            var lastValue = Assert.IsType<IntegerValue>(sensor.LastValue);
            Assert.Equal(2, lastValue.Value);
            Assert.Equal(newValue.Time, lastValue.Time);
            database.Verify(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()), Times.Once);
        }

        [Fact]
        [Trait("Category", "Initialization race")]
        public void TryAddValue_StaleValueDuringHistoryLoad_IsJudgedAgainstLoadedHistory()
        {
            var historyTime = DateTime.UtcNow.AddMinutes(-1);
            var historyBytes = new MemoryPackFormatter().Serialize(
                new IntegerValue { Time = historyTime, Status = SensorStatus.Ok, Value = 1 });

            using var loadEntered = new ManualResetEventSlim(false);
            using var loadGate = new ManualResetEventSlim(false);

            var database = new Mock<IDatabaseCore>();
            database.Setup(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()))
                .Returns(() =>
                {
                    loadEntered.Set();
                    loadGate.Wait(_waitTimeout);
                    return historyBytes;
                });
            database.Setup(db => db.GetFirstValue(It.IsAny<Guid>())).Returns(historyBytes);

            // Singleton: TryAddValue decides by comparing against Storage.LastValue, so the verdict
            // is a direct readout of whether the history was already published when the writer ran.
            var sensor = new IntegerSensorModel(BuildEntity(isSingleton: true), database.Object, null);

            var init = Task.Run(sensor.Initialize);
            Assert.True(loadEntered.Wait(_waitTimeout), "history load never started");

            // Older than the history the in-flight load is about to publish.
            var stale = new IntegerValue { Time = historyTime.AddMinutes(-9), Status = SensorStatus.Ok, Value = 2 };
            var added = true;
            var add = Task.Run(() => { added = sensor.TryAddValue(stale); });

            // Settle, not an assertion: gives the writer time to reach the gate and park on _lock.
            // Pre-fix it runs straight through here instead, which is what the asserts below catch.
            add.Wait(TimeSpan.FromMilliseconds(200));

            loadGate.Set();
            Assert.True(Task.WaitAll(new[] { init, add }, _waitTimeout), "init/add did not finish");

            // Pre-fix the writer met an empty Storage, so singleton dedup had nothing to compare
            // against and stored the stale value as current. Against the loaded history it is
            // correctly recognised as older and dropped.
            Assert.False(added, "stale value was accepted because Storage was still empty");
            var lastValue = Assert.IsType<IntegerValue>(sensor.LastValue);
            Assert.Equal(1, lastValue.Value);
            Assert.Equal(historyTime, lastValue.Time);
        }

        [Fact]
        [Trait("Category", "Initialization race")]
        public void Initialize_ReentrantOnSameThread_DoesNotReloadHistory()
        {
            IntegerSensorModel sensor = null;
            var reentered = false;

            var database = new Mock<IDatabaseCore>();
            database.Setup(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()))
                .Returns(() =>
                {
                    // Stands in for the real re-entry path: a policy evaluated inside the load can
                    // reach SensorTimeout -> SensorExpired -> TryAddValue -> Initialize on THIS
                    // thread, where _isInitialized is still false and Monitor lets the owner back in.
                    if (!reentered)
                    {
                        reentered = true;
                        sensor.Initialize();
                    }

                    return null;
                });

            sensor = new IntegerSensorModel(BuildEntity(), database.Object, null);

            sensor.Initialize();

            Assert.True(reentered, "the re-entry never happened — test no longer exercises the guard");
            database.Verify(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()), Times.Once);
        }

        [Fact]
        [Trait("Category", "Initialization race")]
        public void Initialize_Concurrent_LoadsHistoryOnce()
        {
            using var loadEntered = new ManualResetEventSlim(false);
            using var loadGate = new ManualResetEventSlim(false);

            var database = new Mock<IDatabaseCore>();
            database.Setup(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()))
                .Returns(() =>
                {
                    loadEntered.Set();
                    loadGate.Wait(_waitTimeout);
                    return null;
                });

            var sensor = new IntegerSensorModel(BuildEntity(), database.Object, null);

            var first = Task.Run(sensor.Initialize);
            Assert.True(loadEntered.Wait(_waitTimeout), "history load never started");

            var second = Task.Run(sensor.Initialize);
            // The second caller must wait for the in-flight load, not skip ahead on the early latch.
            Assert.False(second.Wait(TimeSpan.FromMilliseconds(200)),
                "second Initialize returned while the first was still loading");

            loadGate.Set();
            Assert.True(Task.WaitAll(new[] { first, second }, _waitTimeout), "initializations did not finish");

            database.Verify(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()), Times.Once);
        }

        [Fact]
        [Trait("Category", "Initialization race")]
        public void Initialize_LoadFails_LatchesWithoutPerValueRetry()
        {
            var database = new Mock<IDatabaseCore>();
            database.Setup(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()))
                .Throws(new IOException("database is broken"));

            var sensor = new IntegerSensorModel(BuildEntity(), database.Object, null);

            sensor.Initialize(); // must swallow + log, not throw

            // The sensor stays usable and the failed load is NOT retried on every value — the flag
            // latches even on failure (deliberate: one loud error beats a retry storm on a broken DB).
            Assert.True(sensor.TryAddValue(new IntegerValue { Time = DateTime.UtcNow, Status = SensorStatus.Ok, Value = 7 }));
            Assert.True(sensor.TryAddValue(new IntegerValue { Time = DateTime.UtcNow, Status = SensorStatus.Ok, Value = 8 }));

            database.Verify(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()), Times.Once);
        }


        private static SensorEntity BuildEntity(bool isSingleton = false) =>
            new()
            {
                Id = Guid.NewGuid().ToString(),
                ProductId = Guid.NewGuid().ToString(),
                DisplayName = RandomGenerator.GetRandomString(),
                Type = (byte)SensorType.Integer,
                IsSingleton = isSingleton,
            };
    }
}
