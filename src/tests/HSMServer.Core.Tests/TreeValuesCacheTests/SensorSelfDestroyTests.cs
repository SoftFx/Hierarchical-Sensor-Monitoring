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
            // re-evaluate the sensor without a restart.
            var history = SensorTestFactory.History(DateTime.UtcNow.AddDays(-3), 42);

            var database = new Mock<IDatabaseCore>();
            database.SetupSequence(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()))
                .Throws(new IOException("database is broken"))
                .Returns(history);
            database.Setup(db => db.GetFirstValue(It.IsAny<Guid>())).Returns(history);

            var entity = SensorTestFactory.BuildEntity(selfDestroyInterval: _selfDestroyInterval);
            var sensor = new IntegerSensorModel(entity, database.Object, null);

            sensor.Initialize();
            Assert.True(sensor.HistoryLoadFailed, "test premise: the first load failed");
            Assert.False(sensor.ShouldDestroy(), "failed load defers destruction");

            sensor.RetryFailedHistoryLoad(DateTime.UtcNow);

            Assert.True(sensor.IsHistoryLoaded, "retry did not load history after recovery");
            Assert.True(sensor.ShouldDestroy(), "stale history must be destroyable after the retry");
        }

        [Fact]
        [Trait("Category", "Initialization race")]
        public void RetryFailedHistoryLoad_IsRateLimitedToFailsAgain()
        {
            // The latch exists to prevent a per-value retry storm (#1296): a still-broken
            // database must see at most one retry per retry interval, not one per sweep tick.
            var (sensor, database) = BuildSensor(historyTime: DateTime.UtcNow.AddDays(-3));
            database.Setup(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()))
                .Throws(new IOException("database is broken"));

            sensor.Initialize();

            var now = DateTime.UtcNow;
            sensor.RetryFailedHistoryLoad(now);
            sensor.RetryFailedHistoryLoad(now.AddHours(1));
            sensor.RetryFailedHistoryLoad(now.AddHours(2));

            database.Verify(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()), Times.Exactly(2),
                "retry must fire at most once per retry interval");
            Assert.True(sensor.HistoryLoadFailed, "retry against a broken DB must re-latch as failed");

            sensor.RetryFailedHistoryLoad(now.AddHours(25));
            database.Verify(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()), Times.Exactly(3),
                "retry must fire again once the interval has passed");
        }

        [Fact]
        [Trait("Category", "Initialization race")]
        public void RetryFailedHistoryLoad_SuccessfulSensor_IsNoOp()
        {
            var (sensor, database) = BuildSensor(historyTime: DateTime.UtcNow.AddDays(-3));
            sensor.Initialize();

            sensor.RetryFailedHistoryLoad(DateTime.UtcNow);

            database.Verify(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>()), Times.Once,
                "retry must not reload history for a healthy sensor");
            Assert.True(sensor.IsHistoryLoaded);
        }


        private static (IntegerSensorModel Sensor, Mock<IDatabaseCore> DatabaseMock) BuildSensor(DateTime historyTime, bool configureSelfDestroy = true)
        {
            var history = SensorTestFactory.History(historyTime, 42);

            var database = new Mock<IDatabaseCore>();
            database.Setup(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>())).Returns(history);
            database.Setup(db => db.GetFirstValue(It.IsAny<Guid>())).Returns(history);

            var entity = SensorTestFactory.BuildEntity(selfDestroyInterval: configureSelfDestroy ? _selfDestroyInterval : null);

            return (new IntegerSensorModel(entity, database.Object, null), database);
        }
    }
}
