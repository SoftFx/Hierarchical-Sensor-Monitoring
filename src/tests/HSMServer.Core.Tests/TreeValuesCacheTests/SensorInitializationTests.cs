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

        /// <summary>Bounds how long a call may be given to prove it did NOT complete. Asserting
        /// non-completion is the safe polarity: a loaded agent makes it more likely to hold.</summary>
        private static readonly TimeSpan _mustNotComplete = TimeSpan.FromMilliseconds(200);


        [Fact]
        [Trait("Category", "Initialization race")]
        public void TryAddValue_DuringHistoryLoad_WaitsForLoadedStorage()
        {
            using var load = new GatedLoad(History(DateTime.UtcNow.AddMinutes(-5), 1));
            var sensor = new IntegerSensorModel(BuildEntity(), load.Database.Object, null);

            var init = Task.Run(sensor.Initialize);
            Assert.True(load.Entered.Wait(_waitTimeout), "history load never started");

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
            Assert.False(add.Wait(_mustNotComplete),
                "TryAddValue completed while the history load was still in flight");

            load.Gate.Set();
            Assert.True(Task.WaitAll(new[] { init, add }, _waitTimeout), "init/add did not finish");

            Assert.True(added);
            var lastValue = Assert.IsType<IntegerValue>(sensor.LastValue);
            Assert.Equal(2, lastValue.Value);
            Assert.Equal(newValue.Time, lastValue.Time);
            load.Database.Verify(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()), Times.Once);
        }

        [Theory]
        [InlineData(ValueGate.TryUpdateLastValue)]
        [InlineData(ValueGate.CheckTimeout)]
        [Trait("Category", "Initialization race")]
        public void ValueGate_DuringHistoryLoad_WaitsForLoadedStorage(ValueGate gate)
        {
            using var load = new GatedLoad(History(DateTime.UtcNow.AddMinutes(-5), 1));
            var sensor = new IntegerSensorModel(BuildEntity(), load.Database.Object, null);

            var init = Task.Run(sensor.Initialize);
            Assert.True(load.Entered.Wait(_waitTimeout), "history load never started");

            using var callStarted = new ManualResetEventSlim(false);
            var call = Task.Run(() =>
            {
                callStarted.Set();

                var value = new IntegerValue { Time = DateTime.UtcNow, Status = SensorStatus.Ok, Value = 2 };

                switch (gate)
                {
                    case ValueGate.TryUpdateLastValue:
                        sensor.TryUpdateLastValue(value);
                        break;
                    case ValueGate.CheckTimeout:
                        sensor.CheckTimeout();
                        break;
                }
            });

            Assert.True(callStarted.Wait(_waitTimeout), $"{gate} task never started");
            Assert.False(call.Wait(_mustNotComplete),
                $"{gate} completed while the history load was still in flight");

            load.Gate.Set();
            Assert.True(Task.WaitAll(new[] { init, call }, _waitTimeout), "init/call did not finish");
        }

        [Fact]
        [Trait("Category", "Initialization race")]
        public void TryAddValue_StaleValueDuringHistoryLoad_IsJudgedAgainstLoadedHistory()
        {
            var historyTime = DateTime.UtcNow.AddMinutes(-1);

            using var load = new GatedLoad(History(historyTime, 1));
            // Singleton: TryAddValue decides by comparing against Storage.LastValue, so the verdict
            // is a direct readout of whether the history was already published when the writer ran.
            var sensor = new IntegerSensorModel(BuildEntity(isSingleton: true), load.Database.Object, null);

            var init = Task.Run(sensor.Initialize);
            Assert.True(load.Entered.Wait(_waitTimeout), "history load never started");

            // Older than the history the in-flight load is about to publish.
            var stale = new IntegerValue { Time = historyTime.AddMinutes(-9), Status = SensorStatus.Ok, Value = 2 };
            var added = true;
            using var addStarted = new ManualResetEventSlim(false);
            var add = Task.Run(() =>
            {
                addStarted.Set();
                added = sensor.TryAddValue(stale);
            });

            // Pin the overlap rather than assuming it: without these two the writer could run
            // entirely after the load and the outcome assert below would pass without a race.
            Assert.True(addStarted.Wait(_waitTimeout), "TryAddValue task never started");
            Assert.False(add.Wait(_mustNotComplete),
                "TryAddValue completed while the history load was still in flight");

            load.Gate.Set();
            Assert.True(Task.WaitAll(new[] { init, add }, _waitTimeout), "init/add did not finish");

            // Pre-fix the writer met an empty Storage, so singleton dedup had nothing to compare
            // against and stored the stale value as current. Against the loaded history it is
            // correctly recognised as older and dropped. These asserts are order-sensitive and
            // cannot pass by accident, unlike the non-completion bound above.
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
            using var load = new GatedLoad(null);
            var sensor = new IntegerSensorModel(BuildEntity(), load.Database.Object, null);

            var first = Task.Run(sensor.Initialize);
            Assert.True(load.Entered.Wait(_waitTimeout), "history load never started");

            // secondStarted proves the task body ran: without it a thread pool that schedules the
            // task later than the bound below makes the Wait return false for the wrong reason and
            // the test passes having verified nothing.
            using var secondStarted = new ManualResetEventSlim(false);
            var second = Task.Run(() =>
            {
                secondStarted.Set();
                sensor.Initialize();
            });

            Assert.True(secondStarted.Wait(_waitTimeout), "second Initialize task never started");
            // The second caller must wait for the in-flight load, not skip ahead on the early latch.
            Assert.False(second.Wait(_mustNotComplete),
                "second Initialize returned while the first was still loading");

            load.Gate.Set();
            Assert.True(Task.WaitAll(new[] { first, second }, _waitTimeout), "initializations did not finish");

            load.Database.Verify(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()), Times.Once);
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


        /// <summary>
        /// A mocked database whose history read blocks until <see cref="Gate"/> is set, so a test can
        /// hold Initialize() mid-load and act while it is in flight. <see cref="Entered"/> signals
        /// that the load actually reached the database.
        /// </summary>
        private sealed class GatedLoad : IDisposable
        {
            public Mock<IDatabaseCore> Database { get; } = new();

            public ManualResetEventSlim Entered { get; } = new(false);

            public ManualResetEventSlim Gate { get; } = new(false);


            public GatedLoad(byte[] history)
            {
                Database.Setup(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()))
                    .Returns(() =>
                    {
                        Entered.Set();

                        // Throw rather than return: a test that forgets to open the gate would
                        // otherwise get its history 10s later and pass having proved nothing.
                        if (!Gate.Wait(_waitTimeout))
                            throw new TimeoutException("the test never opened the load gate");

                        return history;
                    });

                Database.Setup(db => db.GetFirstValue(It.IsAny<Guid>())).Returns(history);
            }


            public void Dispose()
            {
                Entered.Dispose();
                Gate.Dispose();
            }
        }


        /// <summary>
        /// The other two entry points that gate on _isInitialized (TryAddValue is covered in depth
        /// by the test above). CheckTimeout is the operationally interesting one: it is reached from
        /// ProductModel.CheckTimeout() and BaseNodeModel.TryUpdate, so a settings change during
        /// startup walks every sensor and can park on each in-flight load.
        /// </summary>
        public enum ValueGate
        {
            TryUpdateLastValue,
            CheckTimeout,
        }


        private static byte[] History(DateTime time, int value) =>
            new MemoryPackFormatter().Serialize(
                new IntegerValue { Time = time, Status = SensorStatus.Ok, Value = value });

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
