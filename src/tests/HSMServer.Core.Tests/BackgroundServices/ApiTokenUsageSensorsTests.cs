using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMServer.BackgroundServices;
using System.Linq;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.BackgroundServices
{
    // The eviction state machine of the token-usage monitoring (#1403
    // review, round 5: it is not a "thin composition" — finding 1 lived
    // here, and this suite with a fake collector catches that class
    // directly). The collector is mocked; the created sensor mocks carry
    // IDisposable exactly like the concrete monitoring sensors do.
    public class ApiTokenUsageSensorsTests
    {
        private readonly Mock<IDataCollector> _collector = new();
        private readonly ConcurrentDictionary<string, (Mock<IDisposable> Rate, Mock<IDisposable> Duration)> _created = new();


        public ApiTokenUsageSensorsTests()
        {
            _collector.Setup(c => c.CreateRateSensor(It.IsAny<string>(), It.IsAny<RateSensorOptions>()))
                .Returns((string path, RateSensorOptions _) =>
                {
                    var sensor = new Mock<IMonitoringRateSensor>();
                    var disposable = sensor.As<IDisposable>();
                    _created[$"rate:{path}"] = (disposable, null);
                    return sensor.Object;
                });

            _collector.Setup(c => c.CreateDoubleBarSensor(It.IsAny<string>(), It.IsAny<BarSensorOptions>()))
                .Returns((string path, BarSensorOptions _) =>
                {
                    var sensor = new Mock<IBarSensor<double>>();
                    var disposable = sensor.As<IDisposable>();
                    _created[$"bar:{path}"] = (null, disposable);
                    return sensor.Object;
                });
        }


        private ApiTokenUsageSensors CreateSensors() => new(_collector.Object);

        // Only the per-token sensors (under "By owner") — the aggregate
        // auth-failures counter shares the factories and is permanent.
        private IEnumerable<(Mock<IDisposable> Rate, Mock<IDisposable> Duration)> PerTokenSensors() =>
            _created.Where(pair => pair.Key.Contains("/By owner/")).Select(pair => pair.Value);


        // #1403 review round 5, finding 1 — the regression that shipped in
        // round 4: re-disposing a tombstone was a no-op (the node's _disposed
        // guard returned early and the sensor references were gone), so a
        // collector restart (the self-monitoring toggle) resurrected the dead
        // token's senders forever. The sweep's anti-resurrection pass must
        // re-STOP the instances on every tick.
        [Fact]
        public void Evict_EveryLaterSweep_RestopsTheTombstonedSensors()
        {
            var sensors = CreateSensors();
            var entityId = Guid.NewGuid();

            sensors.AddRestRequest("ops.user", entityId, 1.0);
            sensors.EvictDeadTokens(_ => false); // evict + first stop
            sensors.EvictDeadTokens(_ => false); // anti-resurrection pass
            sensors.EvictDeadTokens(_ => false); // and again

            // Per-token sensors only — the eager aggregate counter (also
            // created through the rate factory) is never disposed.
            foreach (var (rate, duration) in PerTokenSensors())
            {
                // 4 = the evict-stop, the same sweep's tombstone pass, and
                // the two later sweeps' anti-resurrection passes.
                (rate ?? duration).Verify(d => d.Dispose(), Times.Exactly(4));
            }
        }


        // The tombstone is the OCCUPIED-PATH guard: the collector still holds
        // the (stopped) sensors under the token's paths, and a rebuilt node
        // would silently drop every value into their dead instances.
        [Fact]
        public void EvictedToken_IsNeverRecreated()
        {
            var sensors = CreateSensors();
            var entityId = Guid.NewGuid();

            sensors.AddRestRequest("ops.user", entityId, 1.0);
            sensors.EvictDeadTokens(_ => false);

            sensors.AddRestRequest("ops.user", entityId, 1.0); // straggler: dropped, not rebuilt

            // Exactly one creation of each PER-TOKEN sensor (the eager
            // aggregate counter aside — it shares the rate factory).
            _collector.Verify(c => c.CreateRateSensor(It.Is<string>(p => p.Contains("/REST/Request rate")), It.IsAny<RateSensorOptions>()), Times.Once);
            _collector.Verify(c => c.CreateDoubleBarSensor(It.Is<string>(p => p.Contains("/REST/Request duration")), It.IsAny<BarSensorOptions>()), Times.Once);
        }


        [Fact]
        public void LiveToken_IsNeverEvicted()
        {
            var sensors = CreateSensors();
            var entityId = Guid.NewGuid();

            sensors.AddMcpRequest("ops.user", entityId, 1.0);
            sensors.EvictDeadTokens(_ => true); // everyone live

            foreach (var (rate, duration) in PerTokenSensors())
            {
                (rate ?? duration).Verify(d => d.Dispose(), Times.Never);
            }
        }


        // A REST-only token evicts and re-sweeps cleanly: the MCP pair was
        // never created, so the never-used channels must not fire the
        // not-IDisposable diagnostic (a null sensor is nothing to stop) and
        // the used channel's sensors are still re-stopped every sweep.
        [Fact]
        public void SingleChannelToken_EvictsCleanly_OnlyTheUsedChannelHasSensors()
        {
            var sensors = CreateSensors();
            var entityId = Guid.NewGuid();

            sensors.AddRestRequest("ops.user", entityId, 1.0); // REST only
            sensors.EvictDeadTokens(_ => false);
            sensors.EvictDeadTokens(_ => false);

            // Only two sensors were ever created (the REST pair); the MCP
            // pair must not appear even now.
            Assert.Equal(2, PerTokenSensors().Count(pair => pair.Rate is not null || pair.Duration is not null));

            foreach (var (rate, duration) in PerTokenSensors())
                (rate ?? duration).Verify(d => d.Dispose(), Times.Exactly(3)); // evict + same-sweep + next sweep
        }
    }


    // The eviction sweep's composed liveness predicate (#1403 review, round 6):
    // IsTokenLive alone says nothing about the OWNER — deleting a user
    // invalidates the credential without touching the token row, which would
    // leave an immortal subtree under a deleted login. The composition is
    // pinned directly against mocks.
    public class TokenUsageLivenessTests
    {
        [Fact]
        public void LiveTokenWithDeletedOwner_IsDead()
        {
            var entityId = Guid.NewGuid();
            var ownerId = Guid.NewGuid();

            var tokens = new Mock<HSMServer.Authentication.IApiTokenManager>();
            tokens.Setup(t => t.IsTokenLiveByEntityId(entityId)).Returns(true);
            tokens.Setup(t => t.GetTokenByEntityId(entityId))
                .Returns(new HSMServer.Authentication.ApiTokenInfo { EntityId = entityId, OwnerUserId = ownerId });

            var users = new Mock<HSMServer.Authentication.IUserManager>();
            users.Setup(u => u[ownerId]).Returns((HSMServer.Model.Authentication.User)null); // deleted

            Assert.False(TokenUsageLiveness.Compose(tokens.Object, users.Object)(entityId));
        }


        [Fact]
        public void LiveTokenWithExistingOwner_IsLive()
        {
            var entityId = Guid.NewGuid();
            var ownerId = Guid.NewGuid();

            var tokens = new Mock<HSMServer.Authentication.IApiTokenManager>();
            tokens.Setup(t => t.IsTokenLiveByEntityId(entityId)).Returns(true);
            tokens.Setup(t => t.GetTokenByEntityId(entityId))
                .Returns(new HSMServer.Authentication.ApiTokenInfo { EntityId = entityId, OwnerUserId = ownerId });

            var users = new Mock<HSMServer.Authentication.IUserManager>();
            users.Setup(u => u[ownerId]).Returns(new HSMServer.Model.Authentication.User("ops.user"));

            Assert.True(TokenUsageLiveness.Compose(tokens.Object, users.Object)(entityId));
        }
    }
}
