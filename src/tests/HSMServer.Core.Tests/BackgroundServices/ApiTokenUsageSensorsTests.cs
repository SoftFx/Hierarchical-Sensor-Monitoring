using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMServer.BackgroundServices;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.BackgroundServices
{
    // The eviction state machine of the token-usage monitoring (#1403
    // review: not a "thin composition" — the re-disposal regression lived
    // here, and this suite with a fake collector catches that class
    // directly). The collector is mocked; the created sensor mocks carry
    // BOTH stop shapes exactly like the concrete monitoring sensors do —
    // IDisposable (the discarding stop) and the collector's ISensor with
    // StopAsync (the FLUSHING stop) — so the eviction's flush branch is
    // exercised, not just the Dispose fallback (#1403 review).
    public class ApiTokenUsageSensorsTests
    {
        private readonly Mock<IDataCollector> _collector = new();
        private readonly ConcurrentDictionary<string, SensorMocks> _created = new();

        // Both stop surfaces of one sensor double: Dispose is the
        // anti-resurrection re-stop, StopAsync is the eviction's flush.
        private sealed record SensorMocks(Mock<IDisposable> Disposable, Mock<HSMDataCollector.DefaultSensors.ISensor> Stoppable);


        public ApiTokenUsageSensorsTests()
        {
            _collector.Setup(c => c.CreateRateSensor(It.IsAny<string>(), It.IsAny<RateSensorOptions>()))
                .Returns((string path, RateSensorOptions _) => Arm(new Mock<IMonitoringRateSensor>(), path).Object);

            _collector.Setup(c => c.CreateDoubleBarSensor(It.IsAny<string>(), It.IsAny<BarSensorOptions>()))
                .Returns((string path, BarSensorOptions _) => Arm(new Mock<IBarSensor<double>>(), path).Object);
        }


        // The concrete monitoring sensors implement both IDisposable and the
        // collector's ISensor (whose StopAsync FLUSHES the partial bar); the
        // factory interfaces carry neither, so the doubles bolt both on.
        private Mock<T> Arm<T>(Mock<T> sensor, string path) where T : class
        {
            var mocks = new SensorMocks(sensor.As<IDisposable>(), sensor.As<HSMDataCollector.DefaultSensors.ISensor>());

            mocks.Stoppable.Setup(s => s.StopAsync()).Returns(default(ValueTask));

            _created[path] = mocks;

            return sensor;
        }


        private ApiTokenUsageSensors CreateSensors() => new(_collector.Object);

        // Only the per-token sensors (under "By owner") — the aggregate
        // auth-failures counter shares the factories and is permanent.
        private IEnumerable<SensorMocks> PerTokenSensors() =>
            _created.Where(pair => pair.Key.Contains("/By owner/")).Select(pair => pair.Value);


        // #1403 review — the regression an earlier iteration of this PR
        // shipped: re-disposing a tombstone was a no-op (the node's _disposed
        // guard returned early and the sensor references were gone), so a
        // collector restart (the self-monitoring toggle) resurrected the dead
        // token's senders forever. The sweep's anti-resurrection pass must
        // re-STOP the instances on every tick — via Dispose, while the
        // eviction's OWN stop is exactly one flushing StopAsync.
        [Fact]
        public void Evict_EveryLaterSweep_RestopsTheTombstonedSensors()
        {
            var sensors = CreateSensors();
            var entityId = Guid.NewGuid();

            sensors.AddRestRequest("ops.user", entityId, 1.0);
            sensors.EvictDeadTokens(_ => false); // evict + flush stop
            sensors.EvictDeadTokens(_ => false); // anti-resurrection pass
            sensors.EvictDeadTokens(_ => false); // and again

            // Per-token sensors only — the eager aggregate counter (also
            // created through the rate factory) is never stopped.
            foreach (var mocks in PerTokenSensors())
            {
                // The eviction flushes: StopAsync exactly ONCE — a token
                // evicted seconds after serving traffic keeps its last bar
                // period of duration samples.
                mocks.Stoppable.Verify(s => s.StopAsync(), Times.Exactly(1));

                // 3 = the same sweep's tombstone pass and the two later
                // sweeps' anti-resurrection passes (nothing left to flush).
                mocks.Disposable.Verify(d => d.Dispose(), Times.Exactly(3));
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

            foreach (var mocks in PerTokenSensors())
            {
                mocks.Disposable.Verify(d => d.Dispose(), Times.Never);
                mocks.Stoppable.Verify(s => s.StopAsync(), Times.Never);
            }
        }


        // A REST-only token evicts and re-sweeps cleanly: the MCP pair was
        // never created, so the never-used channels must not fire the
        // not-stoppable diagnostic (a null sensor is nothing to stop) and
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
            Assert.Equal(2, PerTokenSensors().Count());

            foreach (var mocks in PerTokenSensors())
            {
                mocks.Stoppable.Verify(s => s.StopAsync(), Times.Exactly(1)); // the eviction flush
                mocks.Disposable.Verify(d => d.Dispose(), Times.Exactly(2)); // same-sweep + next sweep
            }
        }


        // The collector-restart heal (#1403 review): the reset clears
        // the LIVE nodes so each rebuilds on its next request — and the reset
        // must NOT tombstone: a tombstoned id would be refused forever, which
        // is the eviction semantics, not the restart semantics.
        [Fact]
        public void ResetLiveNodes_ClearsWithoutTombstoning_TheTokenRebuilds()
        {
            var sensors = CreateSensors();
            var entityId = Guid.NewGuid();

            sensors.AddRestRequest("ops.user", entityId, 1.0);
            sensors.ResetLiveNodes();

            // The rebuild succeeds — the id was NOT tombstoned.
            sensors.AddRestRequest("ops.user", entityId, 2.0);

            // Two distinct nodes were created (the restart semantics), each
            // with its own REST rate sensor: the rebuilt node got a fresh
            // instance, not a cached dead one.
            _collector.Verify(
                c => c.CreateRateSensor(It.Is<string>(p => p.Contains("/REST/Request rate")), It.IsAny<RateSensorOptions>()),
                Times.Exactly(2));
        }
    }


    // The eviction sweep's composed liveness predicate (#1403 review):
    // IsTokenLive alone says nothing about the OWNER — deleting a user
    // invalidates the credential without touching the token row, which would
    // leave an immortal subtree under a deleted login. The composition is
    // pinned directly against mocks; the owner rides the single
    // TryGetLiveOwner lookup (no ApiTokenInfo projection on the sweep path).
    public class TokenUsageLivenessTests
    {
        private delegate void TryGetLiveOwnerCallback(Guid entityId, out Guid ownerUserId);


        private static Mock<HSMServer.Authentication.IApiTokenManager> HealthyTokensWithLiveOwner(Guid entityId, Guid ownerId)
        {
            var tokens = new Mock<HSMServer.Authentication.IApiTokenManager>();
            tokens.Setup(t => t.IsGenerationStateHealthy).Returns(true);
            tokens.Setup(t => t.TryGetLiveOwner(entityId, out It.Ref<Guid>.IsAny))
                .Callback(new TryGetLiveOwnerCallback((Guid _, out Guid owner) => owner = ownerId))
                .Returns(true);
            return tokens;
        }


        [Fact]
        public void LiveTokenWithDeletedOwner_IsDead()
        {
            var entityId = Guid.NewGuid();
            var ownerId = Guid.NewGuid();

            var tokens = HealthyTokensWithLiveOwner(entityId, ownerId);
            var users = new Mock<HSMServer.Authentication.IUserManager>();
            users.Setup(u => u[ownerId]).Returns((HSMServer.Model.Authentication.User)null); // deleted

            Assert.False(TokenUsageLiveness.Compose(tokens.Object, users.Object)(entityId));
        }


        [Fact]
        public void LiveTokenWithExistingOwner_IsLive()
        {
            var entityId = Guid.NewGuid();
            var ownerId = Guid.NewGuid();

            var tokens = HealthyTokensWithLiveOwner(entityId, ownerId);
            var users = new Mock<HSMServer.Authentication.IUserManager>();
            users.Setup(u => u[ownerId]).Returns(new HSMServer.Model.Authentication.User("ops.user"));

            Assert.True(TokenUsageLiveness.Compose(tokens.Object, users.Object)(entityId));
        }


        // While the boot state is unproven the index answers "not live" for
        // EVERY token, and a tombstone is irreversible for the process
        // lifetime — the sweep must ABSTAIN rather than read a global
        // can't-tell as per-token death (#1403 review; unreachable today:
        // Initialize runs once, before the collector starts).
        [Fact]
        public void UnhealthyGenerationState_TheSweepAbstains_EvenForDeadRecords()
        {
            var entityId = Guid.NewGuid();

            var tokens = new Mock<HSMServer.Authentication.IApiTokenManager>();
            tokens.Setup(t => t.IsGenerationStateHealthy).Returns(false);
            tokens.Setup(t => t.TryGetLiveOwner(entityId, out It.Ref<Guid>.IsAny)).Returns(false);

            var users = new Mock<HSMServer.Authentication.IUserManager>();

            Assert.True(TokenUsageLiveness.Compose(tokens.Object, users.Object)(entityId));
        }
    }
}
