using System;
using System.Collections.Generic;
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


        private static (IntegerSensorModel Sensor, Mock<IDatabaseCore> DatabaseMock) BuildSensor(DateTime historyTime)
        {
            var history = new MemoryPackFormatter().Serialize(
                new IntegerValue { Time = historyTime, Status = SensorStatus.Ok, Value = 42 });

            var database = new Mock<IDatabaseCore>();
            database.Setup(db => db.GetLatestValue(It.IsAny<Guid>(), It.IsAny<long>())).Returns(history);
            database.Setup(db => db.GetFirstValue(It.IsAny<Guid>())).Returns(history);

            var entity = new SensorEntity
            {
                Id = Guid.NewGuid().ToString(),
                ProductId = Guid.NewGuid().ToString(),
                DisplayName = RandomGenerator.GetRandomString(),
                Type = (byte)SensorType.Integer,
                CreationDate = DateTime.UtcNow.AddMonths(-1).Ticks,
                Settings = new Dictionary<string, TimeIntervalEntity>
                {
                    [nameof(BaseSensorModel.Settings.SelfDestroy)] = new((long)TimeInterval.Ticks, _selfDestroyInterval.Ticks),
                },
            };

            return (new IntegerSensorModel(entity, database.Object, null), database);
        }
    }
}
