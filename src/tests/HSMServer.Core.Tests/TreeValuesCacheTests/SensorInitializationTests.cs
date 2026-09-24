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

        /// <summary>Cap for polling the observable start of a Task.Run item (#1450): under the
        /// full suite's parallel-startup storm a loaded agent's pool can be late by far more than
        /// the old fixed 10 s bound, so the cap is raised to 60 s. This is a bigger budget, not
        /// a removed deadline — the deadline is only checked when a pool continuation runs, so
        /// a starved pool still delays the check itself and can outlive the cap.</summary>
        private static readonly TimeSpan _scheduleCap = TimeSpan.FromSeconds(60);

        /// <summary>The gated load must outlive every wait performed while the load is parked:
        /// up to two <see cref="_scheduleCap"/> start polls (the theory) plus their trailing
        /// continuations, which a starved pool runs late, and the short non-completion wait.
        /// Derived from the poll cap (2 x cap + <see cref="_waitTimeout"/> slack) rather than a
        /// fixed number, so a slow pool cannot run the gate out before the polls finish and
        /// misreport the failure as "the load gate was never opened".</summary>
        private static readonly TimeSpan _loadGateCap =
            TimeSpan.FromTicks(2 * _scheduleCap.Ticks + _waitTimeout.Ticks);


        [Fact]
        [Trait("Category", "Initialization race")]
        public async Task TryAddValue_DuringHistoryLoad_WaitsForLoadedStorage()
        {
            using var load = new GatedLoad(SensorTestFactory.History(DateTime.UtcNow.AddMinutes(-5), 1), _loadGateCap);
            var sensor = new IntegerSensorModel(SensorTestFactory.BuildEntity(), load.Database.Object, null);

            var init = Task.Run(sensor.Initialize);
            await TestWait.UntilAsync(() => load.Entered.IsSet, "history load never started", _scheduleCap);

            var newValue = new IntegerValue { Time = DateTime.UtcNow, Status = SensorStatus.Ok, Value = 2 };
            var added = false;
            using var addStarted = new ManualResetEventSlim(false);
            var add = Task.Run(() =>
            {
                addStarted.Set();
                added = sensor.TryAddValue(newValue);
            });

            await TestWait.UntilAsync(() => addStarted.IsSet, "TryAddValue task never started", _scheduleCap);
            // The load is still in flight (the gate is closed), so the writer must be parked on the
            // initialization lock. Pre-fix code latched the flag before reading the database, and
            // this Wait returned true immediately — TryAddValue completed against an empty Storage.
            Assert.False(add.Wait(_mustNotComplete),
                "TryAddValue completed while the history load was still in flight");

            load.Gate.Set();
            Assert.True(Task.WaitAll(new[] { init, add }, _waitTimeout), "init/add did not finish");
            Assert.False(load.TimedOut, "the load gate was never opened");

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
        public async Task ValueGate_DuringHistoryLoad_WaitsForLoadedStorage(ValueGate gate)
        {
            var historyTime = DateTime.UtcNow.AddMinutes(-5);

            using var load = new GatedLoad(SensorTestFactory.History(historyTime, 1), _loadGateCap);
            var sensor = new IntegerSensorModel(SensorTestFactory.BuildEntity(), load.Database.Object, null);

            var init = Task.Run(sensor.Initialize);
            // Poll the observable start precondition under a raised cap instead of the old
            // fixed 10 s Wait: under the full suite's parallel-startup storm the pool can run
            // this Task.Run item far later than 10 s, and the old bound failed the theory
            // though the code under test was correct (#1450). The cap tolerates slow
            // continuations — each poll's deadline is only checked when its continuation runs.
            await TestWait.UntilAsync(() => load.Entered.IsSet, "history load never started", _scheduleCap);

            using var callStarted = new ManualResetEventSlim(false);
            var observedFrom = DateTime.MaxValue;
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

                // Captured the instant the gate returns, before the load could have finished
                // afterwards — this is the assertion that actually encodes the contract.
                observedFrom = sensor.From;
            });

            // Same wait as above — the call task's START is a precondition pinned under the
            // same raised cap, tolerant of slow pool continuations.
            await TestWait.UntilAsync(() => callStarted.IsSet, $"{gate} task never started", _scheduleCap);
            Assert.False(call.Wait(_mustNotComplete),
                $"{gate} completed while the history load was still in flight");

            load.Gate.Set();
            Assert.True(Task.WaitAll(new[] { init, call }, _waitTimeout), "init/call did not finish");
            Assert.False(load.TimedOut, "the load gate was never opened");

            // Storage.Cut(first.Time) runs at the end of the load, so From is the loaded history's
            // timestamp only if the caller really waited. Pre-fix it returned first and saw MinValue.
            Assert.Equal(historyTime, observedFrom);
        }

        [Fact]
        [Trait("Category", "Initialization race")]
        public async Task TryAddValue_StaleValueDuringHistoryLoad_IsJudgedAgainstLoadedHistory()
        {
            var historyTime = DateTime.UtcNow.AddMinutes(-1);

            using var load = new GatedLoad(SensorTestFactory.History(historyTime, 1), _loadGateCap);
            // Singleton: TryAddValue decides by comparing against Storage.LastValue, so the verdict
            // is a direct readout of whether the history was already published when the writer ran.
            var sensor = new IntegerSensorModel(SensorTestFactory.BuildEntity(isSingleton: true), load.Database.Object, null);

            var init = Task.Run(sensor.Initialize);
            await TestWait.UntilAsync(() => load.Entered.IsSet, "history load never started", _scheduleCap);

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
            await TestWait.UntilAsync(() => addStarted.IsSet, "TryAddValue task never started", _scheduleCap);
            Assert.False(add.Wait(_mustNotComplete),
                "TryAddValue completed while the history load was still in flight");

            load.Gate.Set();
            Assert.True(Task.WaitAll(new[] { init, add }, _waitTimeout), "init/add did not finish");
            Assert.False(load.TimedOut, "the load gate was never opened");

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
        public void Initialize_TimeoutOnlyHistory_LoadsWithoutThrowing()
        {
            // Reachable state, not a synthetic one: KeepHistory retention sweeps a quiet sensor's
            // real values and leaves only the timeout marker SetExpiredSnapshot wrote as the newest
            // row. The lookup for the value *before* the marker then returns null.
            var timeoutTime = DateTime.UtcNow.AddMinutes(-30);
            var timeoutBytes = new MemoryPackFormatter().Serialize(
                new IntegerValue { Time = timeoutTime, Status = SensorStatus.Ok, Value = 0, IsTimeout = true });

            var database = new Mock<IDatabaseCore>();
            database.Setup(db => db.GetLatestValue(It.IsAny<Guid>(), DateTime.MaxValue.Ticks)).Returns(timeoutBytes);
            database.Setup(db => db.GetLatestValue(It.IsAny<Guid>(), timeoutTime.Ticks - 1)).Returns((byte[])null);
            database.Setup(db => db.GetFirstValue(It.IsAny<Guid>())).Returns(timeoutBytes);

            var sensor = new IntegerSensorModel(SensorTestFactory.BuildEntity(), database.Object, null);

            sensor.Initialize();

            // Unguarded, Convert(null) throws, the catch swallows it and the finally latches — the
            // sensor is left initialized over an empty Storage with no expiry state, permanently.
            // That is the #1296 symptom, no longer bounded to a startup window.
            Assert.True(sensor.IsExpired, "expiry state was lost — the load threw before setting it");
            // AddValueBase routes a timeout marker to _lastTimeout, not _lastValue.
            Assert.NotNull(sensor.LastTimeout);
            Assert.Equal(timeoutTime, sensor.LastTimeout.Time);
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

            sensor = new IntegerSensorModel(SensorTestFactory.BuildEntity(), database.Object, null);

            sensor.Initialize();

            Assert.True(reentered, "the re-entry never happened — test no longer exercises the guard");
            database.Verify(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()), Times.Once);
        }

        [Fact]
        [Trait("Category", "Initialization race")]
        public async Task Initialize_Concurrent_LoadsHistoryOnce()
        {
            using var load = new GatedLoad(SensorTestFactory.History(DateTime.UtcNow.AddMinutes(-5), 1), _loadGateCap);
            var sensor = new IntegerSensorModel(SensorTestFactory.BuildEntity(), load.Database.Object, null);

            var first = Task.Run(sensor.Initialize);
            await TestWait.UntilAsync(() => load.Entered.IsSet, "history load never started", _scheduleCap);

            // secondStarted proves the task body ran before the non-completion assert below:
            // without it a thread pool that schedules the task late lets that assert pass
            // vacuously and the test ends having verified nothing about the in-flight wait.
            using var secondStarted = new ManualResetEventSlim(false);
            var secondSawHistory = false;
            var second = Task.Run(() =>
            {
                secondStarted.Set();
                sensor.Initialize();
                secondSawHistory = sensor.HasData;
            });

            await TestWait.UntilAsync(() => secondStarted.IsSet, "second Initialize task never started", _scheduleCap);
            // The second caller must wait for the in-flight load, not skip ahead on the early latch.
            Assert.False(second.Wait(_mustNotComplete),
                "second Initialize returned while the first was still loading");

            load.Gate.Set();
            Assert.True(Task.WaitAll(new[] { first, second }, _waitTimeout), "initializations did not finish");
            Assert.False(load.TimedOut, "the load gate was never opened");

            // Times.Once alone does not discriminate — pre-fix the second caller also skipped the
            // load, on the early latch. This does: returning from Initialize() must mean the
            // history is visible, which pre-fix it was not.
            Assert.True(secondSawHistory, "Initialize() returned before the history was visible");
            load.Database.Verify(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()), Times.Once);
        }

        [Fact]
        [Trait("Category", "Initialization race")]
        public void Initialize_LoadFails_LatchesWithoutPerValueRetry()
        {
            var database = new Mock<IDatabaseCore>();
            database.Setup(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()))
                .Throws(new IOException("database is broken"));

            var sensor = new IntegerSensorModel(SensorTestFactory.BuildEntity(), database.Object, null);

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

            /// <summary>Set when the gate was never opened. Throwing instead is not enough: the
            /// throw lands inside Initialize()'s catch, which swallows it and latches an empty
            /// Storage, so a test asserting no post-conditions would still pass. Every test asserts
            /// this is false.</summary>
            public bool TimedOut { get; private set; }

            private readonly TimeSpan _gateWait;


            public GatedLoad(byte[] history, TimeSpan gateWait)
            {
                _gateWait = gateWait;

                Database.Setup(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()))
                    .Returns(() =>
                    {
                        Entered.Set();

                        if (!Gate.Wait(_gateWait))
                        {
                            TimedOut = true;
                            throw new TimeoutException("the test never opened the load gate");
                        }

                        return history;
                    });

                Database.Setup(db => db.GetFirstValue(It.IsAny<Guid>())).Returns(history);
            }


            public void Dispose()
            {
                // Release a parked loader before disposing: on a failed assert above, the load
                // thread would otherwise stay blocked on the gate for the rest of the (large)
                // gate window, tying up a pool thread while the run is already lost. TimedOut
                // staying false is fine — the test has failed by then anyway.
                Gate.Set();

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

    }
}
