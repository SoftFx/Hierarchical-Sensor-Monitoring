using System;
using System.IO;
using HSMCommon.Model;
using HSMDatabase.AccessManager.Formatters;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model;
using HSMServer.Core.Tests.Infrastructure;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.TreeValuesCacheTests
{
    /// <summary>
    /// Pins the self-destroy safety contract (#1328): ShouldDestroy() must not decide on an
    /// uninitialized sensor — an empty Storage makes HasData false and the check falls back to
    /// CreationDate, deleting a live sensor older than its self-destroy interval. It must also
    /// stay a pure predicate: no history load, no policy fan-out, no notifications.
    /// </summary>
    public class SensorSelfDestroyTests
    {
        private static readonly TimeSpan _selfDestroyInterval = TimeSpan.FromHours(1);
        private static readonly TimeSpan _longSelfDestroyInterval = TimeSpan.FromDays(7);


        [Fact]
        [Trait("Category", "Initialization race")]
        public void ShouldDestroy_UninitializedSensorWithFreshHistory_ReturnsFalse()
        {
            var (sensor, _) = BuildSensor(historyTime: DateTime.UtcNow.AddMinutes(-1));

            // The regression from #1328: on an uninitialized sensor HasData is false, so master
            // judged the month-old CreationDate against the 1-hour interval and returned true,
            // deleting a sensor whose history is one minute old.
            Assert.False(sensor.ShouldDestroy(),
                "uninitialized sensor with fresh history was scheduled for destruction");
        }

        [Fact]
        [Trait("Category", "Initialization race")]
        public void ShouldDestroy_InitializedSensorWithFreshHistory_ReturnsFalse()
        {
            var (sensor, _) = BuildSensor(historyTime: DateTime.UtcNow.AddMinutes(-1));
            sensor.Initialize();

            Assert.False(sensor.ShouldDestroy());
        }

        [Fact]
        [Trait("Category", "Initialization race")]
        public void ShouldDestroy_InitializedSensorWithStaleHistory_ReturnsTrue()
        {
            // Control case: proves the fresh-history tests above discriminate — the guard defers
            // the decision, it does not make ShouldDestroy() blindly return false.
            var (sensor, _) = BuildSensor(historyTime: DateTime.UtcNow.AddDays(-3));
            sensor.Initialize();

            Assert.True(sensor.ShouldDestroy());
        }

        [Fact]
        [Trait("Category", "Initialization race")]
        public void ShouldDestroy_FailedHistoryLoad_ReturnsFalse()
        {
            // A failed load latches _isInitialized over an empty Storage (anti-retry-storm), so
            // "IsHistoryLoaded" must mean "history actually loaded", not "a load was attempted" —
            // otherwise this exact sensor is deleted by the same tick's sweep (#1328 review).
            var (sensor, database) = BuildSensor(historyTime: DateTime.UtcNow.AddMinutes(-1));
            database.Setup(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()))
                .Throws(new IOException("database is broken"));

            sensor.Initialize();

            Assert.False(sensor.ShouldDestroy(),
                "sensor with a failed history load was scheduled for destruction");
        }

        [Fact]
        [Trait("Category", "Initialization race")]
        public void ShouldDestroy_RecentTimeoutMarkerOnly_ReturnsFalse()
        {
            // Retention can sweep a quiet sensor's real values and leave only the newest row —
            // the SetExpiredSnapshot timeout marker, which never enters the Storage cache, so
            // HasData is false. The marker's time is still evidence of recent activity; falling
            // back straight to CreationDate destroyed such a sensor weeks early.
            var markerTime = DateTime.UtcNow.AddMinutes(-10);
            var (sensor, database) = BuildSensor(historyTime: markerTime);
            var marker = new MemoryPackFormatter().Serialize(
                new IntegerValue { Time = markerTime, Status = SensorStatus.Ok, Value = 0, IsTimeout = true });
            database.Setup(db => db.GetLatestValue(It.IsAny<Guid>(), DateTime.MaxValue.Ticks)).Returns(marker);
            // No real value before the marker.
            database.Setup(db => db.GetLatestValue(It.IsAny<Guid>(), markerTime.Ticks - 1)).Returns((byte[])null);
            database.Setup(db => db.GetFirstValue(It.IsAny<Guid>())).Returns(marker);

            sensor.Initialize();

            Assert.False(sensor.HasData, "test premise: the marker alone leaves Storage empty");
            Assert.False(sensor.ShouldDestroy(),
                "sensor with only a recent timeout marker was scheduled for destruction");
        }

        [Fact]
        [Trait("Category", "Initialization race")]
        public void ShouldDestroy_FullyClearedHistory_ReturnsFalse()
        {
            // A UI/API "clear history" wipes the cache (HasData false) with no timeout marker;
            // before the Storage.To fallback the very next sweep judged the month-old
            // CreationDate and deleted an actively reporting sensor.
            var (sensor, _) = BuildSensor(historyTime: DateTime.UtcNow.AddMinutes(-1));
            sensor.Initialize();

            sensor.Clear(DateTime.MaxValue);

            Assert.False(sensor.HasData, "test premise: the wipe emptied the cache");
            Assert.False(sensor.ShouldDestroy(),
                "sensor with fully cleared but recent history was scheduled for destruction");
        }

        [Fact]
        [Trait("Category", "Initialization race")]
        public void ShouldDestroy_StaleMarkerAndNewerClearedHistory_UsesNewest()
        {
            // Quiet sensor -> marker on day 1; one report on day 5 (To = day 5); retention then
            // empties the cache, and the later re-expiry writes no marker (SetExpiredSnapshot
            // requires HasData). A priority chain would pick the stale day-1 marker and destroy
            // the sensor a TTL-interval too early; the newest-of must win.
            // 7-day interval: the day-1 marker (-8d) is past it, the day-5 To (-5d) is not —
            // a priority chain picks the marker and destroys, the newest-of does not.
            var (sensor, _) = BuildSensor(historyTime: DateTime.UtcNow.AddDays(-5), interval: _longSelfDestroyInterval);
            sensor.Initialize();

            // Stand-in for the marker written on day 1, older than the loaded history.
            sensor.TryAddValue(new IntegerValue { Time = DateTime.UtcNow.AddDays(-8), Status = SensorStatus.Ok, Value = 0, IsTimeout = true });

            sensor.Clear(DateTime.MaxValue);

            Assert.False(sensor.HasData, "test premise: the wipe emptied the cache");
            Assert.NotNull(sensor.LastTimeout);
            Assert.False(sensor.ShouldDestroy(),
                "stale day-1 marker shadowed the newer day-5 To and destroyed the sensor early");
        }

        [Fact]
        [Trait("Category", "Initialization race")]
        public void ShouldDestroy_CachedValueAndNewerMarker_JudgesOnTheValue()
        {
            // The marker stays confined to the empty-cache fallback on purpose. It records when
            // the SERVER noticed the silence, not when the sensor last reported: GetTimeoutValue
            // stamps Time = UtcNow at observation, so after a maintenance window it carries the
            // restart instant. Letting it float a sensor that still has a cached value would
            // postpone every quiet sensor's cleanup by the server's downtime.
            var (sensor, _) = BuildSensor(historyTime: DateTime.UtcNow.AddDays(-3));
            sensor.Initialize();

            // Stand-in for the marker CheckSensorsTimeout writes on the first post-restart pass.
            var markerTime = DateTime.UtcNow.AddMinutes(-1);
            sensor.TryAddValue(new IntegerValue { Time = markerTime, ReceivingTime = markerTime, Status = SensorStatus.Ok, Value = 0, IsTimeout = true });

            Assert.True(sensor.HasData, "test premise: the loaded value is in the cache");
            Assert.Equal(markerTime, sensor.LastTimeout.Time);
            Assert.True(sensor.ShouldDestroy(),
                "a marker stamped at server-restart time postponed a sensor quiet for three days");
        }

        [Fact]
        [Trait("Category", "Initialization race")]
        public void ShouldDestroy_SelfDestroyNotConfigured_ReturnsFalse()
        {
            // No SelfDestroy in settings: the value resolves to a non-null TimeIntervalModel.None
            // (the == null guard never fires), whose GetShiftedTime is DateTime.MaxValue, so
            // TimeIsUp can never fire — the real "never destroy" path.
            var (sensor, _) = BuildSensor(historyTime: DateTime.UtcNow.AddDays(-3), configureSelfDestroy: false);
            sensor.Initialize();

            Assert.False(sensor.ShouldDestroy());
        }

        [Fact]
        [Trait("Category", "Initialization race")]
        public void ShouldDestroy_UninitializedSensor_HasNoSideEffects()
        {
            var (sensor, database) = BuildSensor(historyTime: DateTime.UtcNow.AddDays(-3));

            var receivedNewValue = false;
            sensor.ReceivedNewValue += _ => receivedNewValue = true;

            var expiredFired = false;
            sensor.Policies.SensorExpired += (_, _) => expiredFired = true;

            var destroyed = sensor.ShouldDestroy();

            // A public bool predicate must not read the database, run the policy fan-out or
            // dispatch events — the Initialize()-inside-ShouldDestroy() variant from PR #1325
            // failed exactly here (TTL-expired alerts for a sensor the next line may delete).
            database.Verify(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()), Times.Never);
            database.Verify(db => db.GetFirstValue(It.IsAny<Guid>()), Times.Never);
            Assert.False(receivedNewValue, "ShouldDestroy() dispatched ReceivedNewValue");
            Assert.False(expiredFired, "ShouldDestroy() fired the SensorExpired fan-out");
            Assert.True(sensor.Notifications.IsEmpty, "ShouldDestroy() produced alert notifications");
            Assert.False(sensor.IsExpired, "ShouldDestroy() set expiry state");
            Assert.False(destroyed);
        }


        [Fact]
        [Trait("Category", "Initialization race")]
        public void RetryFailedHistoryLoad_AfterDbRecovers_EnablesSelfDestroy()
        {
            // #1344 acceptance: a transient LevelDB error during a lazily triggered load used to
            // disable self-destroy until restart. After the DB recovers, the bounded retry must
            // re-evaluate the sensor without a restart. The dead sensor's newest DB row is the
            // SetExpiredSnapshot marker (the usual newest row for a sensor that went quiet), so
            // the write-free retry records the marker time as the activity floor and
            // ShouldDestroy judges on that.
            var staleMarkerTime = DateTime.UtcNow.AddDays(-3);
            var marker = new MemoryPackFormatter().Serialize(
                new IntegerValue { Time = staleMarkerTime, ReceivingTime = staleMarkerTime, Status = SensorStatus.Ok, Value = 0, IsTimeout = true });

            var database = new Mock<IDatabaseCore>();
            database.SetupSequence(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()))
                .Throws(new IOException("database is broken"))
                .Returns(marker);
            database.Setup(db => db.GetFirstValue(It.IsAny<Guid>())).Returns(marker);

            var entity = SensorTestFactory.BuildEntity(selfDestroyInterval: _selfDestroyInterval);
            var sensor = new IntegerSensorModel(entity, database.Object, null);

            sensor.Initialize();
            Assert.True(sensor.HistoryLoadFailed, "test premise: the first load failed");
            Assert.False(sensor.ShouldDestroy(), "failed load defers destruction");

            // The sweep that just observed the failure must not retry back-to-back — the first
            // retry only has to clear one sweep period after the failure.
            sensor.RetryFailedHistoryLoad(DateTime.UtcNow);
            Assert.True(sensor.HistoryLoadFailed, "a same-tick retry fired against the failed load");

            sensor.RetryFailedHistoryLoad(DateTime.UtcNow.AddHours(2));

            Assert.True(sensor.IsHistoryLoaded, "retry did not load history after recovery");
            Assert.Null(sensor.LastTimeout);
            Assert.Equal(staleMarkerTime, sensor.To);
            Assert.True(sensor.ShouldDestroy(), "stale history must be destroyable after the retry");
        }

        [Fact]
        [Trait("Category", "Initialization race")]
        public void RetryFailedHistoryLoad_StillBrokenDb_IsRateLimited()
        {
            // The latch exists to prevent a per-value retry storm (#1296): a still-broken
            // database must see at most one retry per backoff interval, not one per sweep tick.
            // The first retry clears one sweep period measured from the FAILURE, later retries
            // the growing delay measured from the previous retry.
            var (sensor, database) = BuildSensor(historyTime: DateTime.UtcNow.AddDays(-3));
            database.Setup(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()))
                .Throws(new IOException("database is broken"));

            sensor.Initialize();

            var now = DateTime.UtcNow;

            // Same tick as the failure — suppressed (one sweep period has not passed).
            Assert.Equal(HistoryLoadRetryResult.Suppressed, sensor.RetryFailedHistoryLoad(now));
            database.Verify(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()), Times.Once,
                "a same-tick retry hit the database back-to-back with the failed load");

            Assert.Equal(HistoryLoadRetryResult.Failed, sensor.RetryFailedHistoryLoad(now.AddHours(1)));
            Assert.Equal(HistoryLoadRetryResult.Suppressed, sensor.RetryFailedHistoryLoad(now.AddHours(2)));

            database.Verify(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()), Times.Exactly(2),
                "retry must fire at most once per backoff interval");
            Assert.True(sensor.HistoryLoadFailed, "retry against a broken DB must re-latch as failed");

            Assert.Equal(HistoryLoadRetryResult.Failed, sensor.RetryFailedHistoryLoad(now.AddHours(25)));
            database.Verify(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()), Times.Exactly(3),
                "retry must fire again once the interval has passed");
        }

        [Fact]
        [Trait("Category", "Initialization race")]
        public void RetryFailedHistoryLoad_StillBrokenDb_BacksOffFromOneHourTowardsTheDailyCap()
        {
            // The backoff must degrade gradually: a flat 1h -> 24h step costs a sensor a full
            // day of disabled self-destroy for any outage that outlives its first hour, even
            // though the sweep runs hourly. Schedule: 1h, then 2h, 4h, 8h, 16h, then the 24h cap.
            var (sensor, database) = BuildSensor(historyTime: DateTime.UtcNow.AddDays(-3));
            database.Setup(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()))
                .Throws(new IOException("database is broken"));

            sensor.Initialize();

            var failedAt = DateTime.UtcNow;
            // Each rung: the sweep one hour early is suppressed, the sweep on time fires.
            var schedule = new[] { 1d, 3d, 7d, 15d, 31d, 55d };

            for (var rung = 0; rung < schedule.Length; rung++)
            {
                var due = failedAt.AddHours(schedule[rung]);

                Assert.True(sensor.RetryFailedHistoryLoad(due.AddHours(-1)) is HistoryLoadRetryResult.Suppressed,
                    $"retry {rung + 1} fired an hour before its backoff elapsed");
                Assert.True(sensor.RetryFailedHistoryLoad(due) is HistoryLoadRetryResult.Failed,
                    $"retry {rung + 1} did not fire once its backoff elapsed");
            }

            // The last two rungs are 24h apart: the cap holds, so a permanently broken database
            // still sees at most one attempt per sensor per day (#1296).
            Assert.Equal(24d, schedule[^1] - schedule[^2]);
            database.Verify(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()),
                Times.Exactly(schedule.Length + 1), "one failed load plus one DB read per rung");
        }

        [Fact]
        [Trait("Category", "Initialization race")]
        public void RetryFailedHistoryLoad_ReportsTheOutcomeTheSweepBudgetsOn()
        {
            // The sweep's per-sweep cap is charged on Failed alone. Charging Suppressed too lets
            // the sensors early in the stable enumeration order exhaust the cap sweep after
            // sweep and permanently starve everything past it; charging Loaded makes a large
            // latched set drain at MaxHistoryLoadRetriesPerSweep sensors per hour after the
            // database is already healthy.
            var (healthy, _) = BuildSensor(historyTime: DateTime.UtcNow.AddDays(-3));
            healthy.Initialize();

            Assert.Equal(HistoryLoadRetryResult.Suppressed, healthy.RetryFailedHistoryLoad(DateTime.UtcNow.AddHours(2)));

            var (broken, brokenDb) = BuildSensor(historyTime: DateTime.UtcNow.AddDays(-3));
            brokenDb.Setup(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()))
                .Throws(new IOException("database is broken"));
            broken.Initialize();

            var now = DateTime.UtcNow;

            Assert.Equal(HistoryLoadRetryResult.Suppressed, broken.RetryFailedHistoryLoad(now));
            Assert.Equal(HistoryLoadRetryResult.Failed, broken.RetryFailedHistoryLoad(now.AddHours(1)));
            Assert.Equal(HistoryLoadRetryResult.Suppressed, broken.RetryFailedHistoryLoad(now.AddHours(1)));

            var (recovering, recoveringDb) = BuildSensor(historyTime: DateTime.UtcNow.AddDays(-3));
            recoveringDb.SetupSequence(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()))
                .Throws(new IOException("database is broken"))
                .Returns(SensorTestFactory.History(DateTime.UtcNow.AddDays(-3), 42));
            recovering.Initialize();

            Assert.Equal(HistoryLoadRetryResult.Loaded, recovering.RetryFailedHistoryLoad(DateTime.UtcNow.AddHours(2)));
        }

        [Fact]
        [Trait("Category", "Initialization race")]
        public void RetryFailedHistoryLoad_ClockBehindTheFailedLoad_SuppressesAndReAnchors()
        {
            // The sweep reads the clock per sensor for this reason: a sensor whose lazy
            // Initialize() fails WHILE the sweep runs stamps its attempt clock from the real
            // clock, so a caller holding an earlier instant sees a stamp in the future. That is
            // not "overdue" — treating it so hits the database that just failed back-to-back and
            // spends a backoff rung. It suppresses and re-anchors, so a large backwards clock
            // step cannot park the sensor until the wall clock catches up either.
            var (sensor, database) = BuildSensor(historyTime: DateTime.UtcNow.AddDays(-3));
            database.Setup(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()))
                .Throws(new IOException("database is broken"));

            sensor.Initialize();
            var failedAt = DateTime.UtcNow;

            Assert.Equal(HistoryLoadRetryResult.Suppressed, sensor.RetryFailedHistoryLoad(failedAt.AddMinutes(-1)));
            database.Verify(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()), Times.Once,
                "a clock behind the failed load fired a back-to-back retry");

            // Re-anchored to the back-dated instant: one delay later the retry is due again.
            Assert.Equal(HistoryLoadRetryResult.Failed, sensor.RetryFailedHistoryLoad(failedAt.AddHours(1)));
        }

        [Fact]
        [Trait("Category", "Initialization race")]
        public void RetryFailedHistoryLoad_HotSensor_DoesNotTouchLiveStorageOrPolicies()
        {
            // The motivating scenario (#1344): the lazily triggered load fails, the sensor keeps
            // reporting, the DB recovers, the sweep retries. The retry must only latch
            // _historyLoaded and record the activity floor — no duplicate DB copy of the live
            // value, no timeout-marker IsExpired forced onto a reporting sensor, no policy
            // fan-out re-entering TryAddValue, not even the pre-marker value lookup the cold
            // branch does, and no write to _lastTimeout, which live ingestion owns.
            var staleMarkerTime = DateTime.UtcNow.AddDays(-2);
            var marker = new MemoryPackFormatter().Serialize(
                new IntegerValue { Time = staleMarkerTime, ReceivingTime = staleMarkerTime, Status = SensorStatus.Ok, Value = 0, IsTimeout = true });
            // Reachable by the cold branch (GetLatestValue(marker.Time - 1)): a real value just
            // before the marker. Its presence makes the fan-out branch genuinely reachable, so
            // the asserts below discriminate the guard instead of passing on a null lookup.
            var preMarkerValue = new MemoryPackFormatter().Serialize(
                new IntegerValue { Time = staleMarkerTime.AddMinutes(-1), Status = SensorStatus.Ok, Value = 9 });

            var database = new Mock<IDatabaseCore>();
            database.SetupSequence(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()))
                .Throws(new IOException("database is broken"))
                .Returns(marker) // newest DB row: a timeout marker older than the live value
                .Returns(preMarkerValue);
            database.Setup(db => db.GetFirstValue(It.IsAny<Guid>())).Returns(marker);

            var entity = SensorTestFactory.BuildEntity(selfDestroyInterval: _selfDestroyInterval);
            var sensor = new IntegerSensorModel(entity, database.Object, null);

            var liveValue = new IntegerValue { Time = DateTime.UtcNow.AddMinutes(-1), Status = SensorStatus.Ok, Value = 7 };
            Assert.True(sensor.TryAddValue(liveValue), "test premise: the live value was accepted");
            Assert.True(sensor.HistoryLoadFailed, "test premise: the lazy load failed");

            var expiredFired = false;
            sensor.Policies.SensorExpired += (_, _) => expiredFired = true;

            sensor.RetryFailedHistoryLoad(DateTime.UtcNow.AddHours(2));

            Assert.True(sensor.IsHistoryLoaded, "retry must latch the history load for a hot sensor");
            Assert.False(sensor.IsExpired, "stale timeout marker forced IsExpired onto a reporting sensor");
            Assert.False(expiredFired, "retry ran the SensorExpired fan-out from the sweep");
            // Stored content, not just identity: neither the marker nor the pre-marker DB row
            // may appear in a live sensor's Storage.
            Assert.Same(liveValue, sensor.LastValue);
            Assert.Same(liveValue, sensor.LastDbValue);
            // _lastTimeout belongs to ingestion: the retry records the marker's time as the
            // activity floor instead of racing the value the live path may be writing.
            Assert.Null(sensor.LastTimeout);
            // Direct discriminator for the guard: the pre-marker lookup is cold-branch-only.
            database.Verify(db => db.GetLatestValue(It.IsAny<Guid>(), staleMarkerTime.Ticks - 1), Times.Never,
                "retry ran the cold-load pre-marker lookup against a live sensor");
        }

        [Fact]
        [Trait("Category", "Initialization race")]
        public void RetryFailedHistoryLoad_LiveSensorWithNullLastValue_DoesNotRebuildStorage()
        {
            // A null LastValue does NOT mean "no live ingestion" (review finding on #1346): a
            // sensor fed only timeout markers, or one whose cache a retention purge emptied
            // mid-tick, keeps LastValue null while being demonstrably live. The retry mode is
            // passed explicitly, so this sensor must take the safe branch, not replay the DB
            // copy of history into live Storage.
            var staleMarkerTime = DateTime.UtcNow.AddDays(-2);
            var staleMarker = new MemoryPackFormatter().Serialize(
                new IntegerValue { Time = staleMarkerTime, ReceivingTime = staleMarkerTime, Status = SensorStatus.Ok, Value = 0, IsTimeout = true });

            var database = new Mock<IDatabaseCore>();
            database.SetupSequence(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()))
                .Throws(new IOException("database is broken"))
                .Returns(staleMarker); // newest DB row: a marker older than the live one
            database.Setup(db => db.GetFirstValue(It.IsAny<Guid>())).Returns(staleMarker);

            var entity = SensorTestFactory.BuildEntity(selfDestroyInterval: _selfDestroyInterval);
            var sensor = new IntegerSensorModel(entity, database.Object, null);

            // Live ingestion: only timeout markers flow in, so LastValue stays null.
            var liveMarkerTime = DateTime.UtcNow.AddMinutes(-1);
            var liveMarker = new IntegerValue { Time = liveMarkerTime, ReceivingTime = liveMarkerTime, Status = SensorStatus.Ok, Value = 0, IsTimeout = true };
            Assert.True(sensor.TryAddValue(liveMarker), "test premise: the live marker was accepted");
            Assert.Null(sensor.LastValue);
            Assert.False(sensor.HasData);
            Assert.True(sensor.HistoryLoadFailed, "test premise: the lazy load failed");

            sensor.RetryFailedHistoryLoad(DateTime.UtcNow.AddHours(2));

            Assert.True(sensor.IsHistoryLoaded);
            Assert.True(sensor.LastValue is null, "retry replayed DB history into a live sensor's Storage");
            Assert.False(sensor.HasData, "retry wrote into a live sensor's cache");
            Assert.False(sensor.IsExpired, "stale timeout marker forced IsExpired onto a reporting sensor");
            // Newest-wins guard: the stale DB marker must not shadow the fresher live one.
            Assert.Equal(liveMarkerTime, sensor.LastTimeout.Time);
            database.Verify(db => db.GetLatestValue(It.IsAny<Guid>(), staleMarkerTime.Ticks - 1), Times.Never,
                "retry ran the cold-load pre-marker lookup against a live sensor");
            // End to end: ShouldDestroy judges on the live marker, not on CreationDate.
            Assert.False(sensor.ShouldDestroy(), "live sensor was scheduled for destruction");
        }

        [Fact]
        [Trait("Category", "Initialization race")]
        public void RetryFailedHistoryLoad_NewestRowIsFreshRealValue_DoesNotDestroy()
        {
            // When the newest DB row is a real value — no TTL configured, or the sensor went
            // quiet without expiring — the retry restores no timeout marker. Without the
            // Storage.To activity floor the latched _historyLoaded lets ShouldDestroy() fall
            // through to CreationDate and delete an established sensor whose newest value is
            // minutes old: the #1328 regression, reopened through the retry path.
            var freshValueTime = DateTime.UtcNow.AddMinutes(-1);
            var freshValue = SensorTestFactory.History(freshValueTime, 42);

            var database = new Mock<IDatabaseCore>();
            database.SetupSequence(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()))
                .Throws(new IOException("database is broken"))
                .Returns(freshValue);
            database.Setup(db => db.GetFirstValue(It.IsAny<Guid>())).Returns(freshValue);

            var entity = SensorTestFactory.BuildEntity(selfDestroyInterval: _selfDestroyInterval);
            var sensor = new IntegerSensorModel(entity, database.Object, null);

            sensor.Initialize();
            Assert.True(sensor.HistoryLoadFailed, "test premise: the first load failed");

            sensor.RetryFailedHistoryLoad(DateTime.UtcNow.AddHours(2));

            Assert.True(sensor.IsHistoryLoaded, "retry did not latch the history load");
            Assert.False(sensor.HasData, "retry must not rebuild the live cache");
            Assert.Equal(freshValueTime, sensor.To); // the restored activity floor
            Assert.False(sensor.ShouldDestroy(), "retry destroyed a sensor whose newest value is one minute old");
        }

        [Fact]
        [Trait("Category", "Initialization race")]
        public void RetryFailedHistoryLoad_AggregatedNewestRow_UsesItsLastReceivingTime()
        {
            // An AggregateValues sensor folds a run of identical samples into one row whose Time
            // stays pinned at the first sample while LastReceivingTime advances, and that row is
            // re-persisted. ShouldDestroy() judges a cached value on LastUpdateTime, so the
            // restored floor has to use the same field — reading Time would date a week-long
            // aggregate to its first sample and destroy a sensor that reported an hour ago.
            var firstSampleTime = DateTime.UtcNow.AddDays(-6);
            var lastReceivingTime = DateTime.UtcNow.AddMinutes(-10);
            var aggregatedRow = SensorTestFactory.History(firstSampleTime, 42, lastReceivingTime);

            var database = new Mock<IDatabaseCore>();
            database.SetupSequence(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()))
                .Throws(new IOException("database is broken"))
                .Returns(aggregatedRow);
            database.Setup(db => db.GetFirstValue(It.IsAny<Guid>())).Returns(aggregatedRow);

            // 1-hour interval: the row's Time is days past it, its LastReceivingTime is not,
            // so the verdict discriminates which field the floor was taken from.
            var entity = SensorTestFactory.BuildEntity(selfDestroyInterval: _selfDestroyInterval);
            var sensor = new IntegerSensorModel(entity, database.Object, null);

            sensor.Initialize();
            Assert.True(sensor.HistoryLoadFailed, "test premise: the lazy load failed");

            Assert.Equal(HistoryLoadRetryResult.Loaded, sensor.RetryFailedHistoryLoad(DateTime.UtcNow.AddHours(2)));

            Assert.Equal(lastReceivingTime, sensor.To);
            Assert.False(sensor.ShouldDestroy(),
                "the floor dated an aggregated row to its first sample and destroyed a live sensor");
        }

        [Fact]
        [Trait("Category", "Initialization race")]
        public void RetryFailedHistoryLoad_SameTickAsFailedEagerLoad_DoesNotBurnBudget()
        {
            // CheckSensorsHistoryAsync eagerly Initialize()s every sensor immediately before
            // RunSensorsSelfDestroyAsync in the same ClearDatabaseService tick. A load failing
            // there must not be retried seconds later against the still-broken DB: that
            // back-to-back attempt burns the sensor's retry budget and turns a 90-second outage
            // into hours of disabled self-destroy.
            var history = SensorTestFactory.History(DateTime.UtcNow.AddDays(-3), 42);

            var database = new Mock<IDatabaseCore>();
            database.SetupSequence(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()))
                .Throws(new IOException("database is broken"))
                .Returns(history);
            database.Setup(db => db.GetFirstValue(It.IsAny<Guid>())).Returns(history);

            // A fresh CreationDate makes the final assert discriminate: the sensor is young
            // enough that CreationDate alone never satisfies the interval, so a "destroy"
            // verdict can only come from the Storage.To floor the retry restores.
            var entity = SensorTestFactory.BuildEntity(selfDestroyInterval: _selfDestroyInterval, creationDate: DateTime.UtcNow.AddMinutes(-5));
            var sensor = new IntegerSensorModel(entity, database.Object, null);

            sensor.Initialize(); // the eager load fails — T
            sensor.RetryFailedHistoryLoad(DateTime.UtcNow); // the same tick's sweep, seconds later

            database.Verify(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()), Times.Once,
                "the sweep retried back-to-back with the failed eager load");

            // The budget survived: the next sweep after recovery retries and succeeds.
            sensor.RetryFailedHistoryLoad(DateTime.UtcNow.AddHours(2));

            Assert.True(sensor.IsHistoryLoaded, "the same-tick retry burned the 24h budget");
            Assert.True(sensor.ShouldDestroy(), "stale history must be destroyable after the retry");
        }

        [Fact]
        [Trait("Category", "Initialization race")]
        public void RetryFailedHistoryLoad_RestoredActivityFloor_SurvivesAnOlderIncomingValue()
        {
            // A retried sensor has an empty cache, so AddValueBase's newest-wins guard has no
            // _lastValue to compare against and accepts the next value whatever its timestamp —
            // a reconnecting collector flushing a stale queue, a client with a skewed clock.
            // Two things must survive that: To must not be dragged back below the newest row the
            // retry certified (the floor is a field of its own, merged into To at read time, so
            // it can be outgrown but never overwritten), and ShouldDestroy() must not switch to
            // the stale value's LastUpdate now that HasData flipped true.
            var newestRowTime = DateTime.UtcNow.AddMinutes(-30);
            var newestRow = SensorTestFactory.History(newestRowTime, 42);

            var database = new Mock<IDatabaseCore>();
            database.SetupSequence(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()))
                .Throws(new IOException("database is broken"))
                .Returns(newestRow);
            database.Setup(db => db.GetFirstValue(It.IsAny<Guid>())).Returns(newestRow);

            var entity = SensorTestFactory.BuildEntity(selfDestroyInterval: _selfDestroyInterval);
            var sensor = new IntegerSensorModel(entity, database.Object, null);

            sensor.Initialize();
            Assert.True(sensor.HistoryLoadFailed, "test premise: the lazy load failed");
            Assert.Equal(HistoryLoadRetryResult.Loaded, sensor.RetryFailedHistoryLoad(DateTime.UtcNow.AddHours(2)));
            Assert.Equal(newestRowTime, sensor.To);
            Assert.False(sensor.HasData, "test premise: the retry leaves the value cache empty");

            var staleValueTime = DateTime.UtcNow.AddDays(-3);
            Assert.True(sensor.TryAddValue(new IntegerValue { Time = staleValueTime, Status = SensorStatus.Ok, Value = 7 }),
                "test premise: an out-of-order value is accepted while LastValue is null");
            Assert.True(sensor.HasData, "test premise: the stale value flipped HasData");

            Assert.Equal(newestRowTime, sensor.To);
            Assert.False(sensor.ShouldDestroy(),
                "one out-of-order value hid the restored activity floor and destroyed the sensor");
        }

        [Fact]
        [Trait("Category", "Initialization race")]
        public void TryAddValue_StaleTimeoutMarker_DoesNotEnterValueCache()
        {
            // Pins the AddValueBase fall-through fix (review finding on #1346): a timeout
            // marker that loses the newest-wins comparison is stale (a duplicate or
            // out-of-order marker) and must be dropped — enqueueing it flipped HasData and
            // installed a timeout value as LastValue, which ShouldDestroy()'s HasData branch
            // then judged on.
            var markerTime = DateTime.UtcNow.AddMinutes(-10);
            var (sensor, database) = BuildSensor(historyTime: markerTime);
            var marker = new MemoryPackFormatter().Serialize(
                new IntegerValue { Time = markerTime, ReceivingTime = markerTime, Status = SensorStatus.Ok, Value = 0, IsTimeout = true });
            database.Setup(db => db.GetLatestValue(It.IsAny<Guid>(), DateTime.MaxValue.Ticks)).Returns(marker);
            // No real value before the marker.
            database.Setup(db => db.GetLatestValue(It.IsAny<Guid>(), markerTime.Ticks - 1)).Returns((byte[])null);
            database.Setup(db => db.GetFirstValue(It.IsAny<Guid>())).Returns(marker);

            sensor.Initialize();
            Assert.False(sensor.HasData, "test premise: the marker alone leaves Storage empty");

            // A marker older than the loaded one: loses the newest-wins comparison.
            var staleMarker = new IntegerValue
            {
                Time = markerTime.AddMinutes(-1),
                ReceivingTime = markerTime.AddMinutes(-1),
                Status = SensorStatus.Ok,
                Value = 0,
                IsTimeout = true,
            };
            Assert.True(sensor.TryAddValue(staleMarker), "test premise: the timeout path accepts it");

            Assert.False(sensor.HasData, "a stale timeout marker was enqueued as a regular value");
            Assert.Null(sensor.LastValue);
            Assert.Equal(markerTime, sensor.LastTimeout.Time);
        }

        [Fact]
        [Trait("Category", "Initialization race")]
        public void HistoryRestoredByRetry_ReportsTheDegradedStateUntilIngestionRefillsTheCache()
        {
            // A successful retry drops the sensor out of the sweep's failed-load warning while
            // its cache, Status, IsExpired and TTL clocks are still unset. The sweep keeps
            // logging it under a separate line until real ingestion refills the cache.
            var freshValue = SensorTestFactory.History(DateTime.UtcNow.AddMinutes(-1), 42);

            var database = new Mock<IDatabaseCore>();
            database.SetupSequence(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()))
                .Throws(new IOException("database is broken"))
                .Returns(freshValue);
            database.Setup(db => db.GetFirstValue(It.IsAny<Guid>())).Returns(freshValue);

            var entity = SensorTestFactory.BuildEntity(selfDestroyInterval: _selfDestroyInterval);
            var sensor = new IntegerSensorModel(entity, database.Object, null);

            sensor.Initialize();
            Assert.False(sensor.HistoryRestoredByRetry, "a failed load is reported as failed, not as restored");

            Assert.Equal(HistoryLoadRetryResult.Loaded, sensor.RetryFailedHistoryLoad(DateTime.UtcNow.AddHours(2)));

            Assert.False(sensor.HistoryLoadFailed, "test premise: the retry succeeded");
            Assert.True(sensor.HistoryRestoredByRetry, "a retry-restored sensor with an empty cache went silent");

            sensor.TryAddValue(new IntegerValue { Time = DateTime.UtcNow, Status = SensorStatus.Ok, Value = 7 });

            Assert.False(sensor.HistoryRestoredByRetry, "the sensor is no longer degraded once it reports");

            // A later retention pass or history clear empties the cache again. That must not
            // resurrect the warning months after the sensor recovered.
            sensor.Clear(DateTime.MaxValue);

            Assert.False(sensor.HasData, "test premise: the clear emptied the cache");
            Assert.False(sensor.HistoryRestoredByRetry, "a routine history clear resurrected the degraded warning");
        }

        [Fact]
        [Trait("Category", "Initialization race")]
        public void RetryFailedHistoryLoad_SuccessfulSensor_IsNoOp()
        {
            var (sensor, database) = BuildSensor(historyTime: DateTime.UtcNow.AddDays(-3));
            sensor.Initialize();

            sensor.RetryFailedHistoryLoad(DateTime.UtcNow.AddHours(2));

            database.Verify(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()), Times.Once,
                "retry must not reload history for a healthy sensor");
            Assert.True(sensor.IsHistoryLoaded);
        }


        private static (IntegerSensorModel Sensor, Mock<IDatabaseCore> DatabaseMock) BuildSensor(DateTime historyTime, bool configureSelfDestroy = true, TimeSpan? interval = null)
        {
            var history = SensorTestFactory.History(historyTime, 42);

            var database = new Mock<IDatabaseCore>();
            database.Setup(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>())).Returns(history);
            database.Setup(db => db.GetFirstValue(It.IsAny<Guid>())).Returns(history);

            var entity = SensorTestFactory.BuildEntity(selfDestroyInterval: configureSelfDestroy ? (interval ?? _selfDestroyInterval) : null);

            return (new IntegerSensorModel(entity, database.Object, null), database);
        }
    }
}
