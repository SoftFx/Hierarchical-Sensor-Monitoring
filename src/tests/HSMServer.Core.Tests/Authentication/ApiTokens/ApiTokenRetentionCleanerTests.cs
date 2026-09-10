using System;
using System.Linq;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.Core.Tests.DatabaseTests;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.ServerConfiguration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Authentication.ApiTokens
{
    // Bounded retention sweep over the durable API-token state: dead token rows and
    // security events older than their independent windows are removed (live rows never),
    // orphan rows wait one window from first observation, every pass is bounded, and a
    // storage failure skips to the next pass instead of wedging the sweep.
    public class ApiTokenRetentionCleanerTests : DatabaseCoreTestsBase<ApiTokenRetentionCleanerTests.Fixture>, IClassFixture<DatabaseRegisterFixture>
    {
        private static readonly Guid OwnerId = Guid.NewGuid();

        // Fixed clock for the security-event cutoffs (exact-cutoff events must survive);
        // the token-row scenarios use times relative to the real creation moment.
        // RELATIVE on purpose: a hardcoded calendar date rots — once the real clock passes
        // it plus the default 30-day retention, leftover events become eligible in tests
        // that pinned nothing, and the suite starts failing on a date.
        private static readonly DateTime Now = DateTime.UtcNow.Date.AddHours(10);

        // Pinned event retention for the token-row tests (below the config's upper
        // bound): long enough that no leftover event row from another test in this
        // shared LevelDB fixture is ever eligible, so their exact-tuple asserts cannot
        // pick up foreign rows.
        private static readonly TimeSpan EventsPinnedOff = TimeSpan.FromDays(365 * 9);


        public ApiTokenRetentionCleanerTests(Fixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture) { }


        private ApiTokenManager CreateManager() =>
            new(_databaseCoreManager.DatabaseCore, NullLogger<ApiTokenManager>.Instance);

        private static ApiTokenRetentionCleaner CreateCleaner(ApiTokensConfig config,
            HSMServer.Core.DataLayer.IDatabaseCore db, ApiTokenManager manager) =>
            new(db, manager, config, NullLogger<ApiTokenRetentionCleaner>.Instance);


        [Fact]
        public void DeadRows_PastTheWindow_AreRemoved_LiveRowsStay()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            var retention = TimeSpan.FromMinutes(10);
            var cleaner = CreateCleaner(new ApiTokensConfig { TokenRecordRetention = retention, SecurityEventRetention = EventsPinnedOff },
                _databaseCoreManager.DatabaseCore, manager);

            var realNow = DateTime.UtcNow;

            Assert.True(manager.TryCreateToken(OwnerId, "live", readOnly: false, "test", out var live, out _));
            Assert.True(manager.TryCreateToken(OwnerId, "revoked", readOnly: false, "test", out var revoked, out _));
            Assert.True(manager.TryRevokeToken(revoked.EntityId, "test", "cleanup test", out _));
            Assert.True(manager.TryCreateToken(OwnerId, "still-alive", readOnly: false, "test", out _, out _));

            // Halfway through the window: nothing is eligible yet.
            var earlyResult = cleaner.RunOnce(realNow.AddMinutes(5));

            Assert.Equal((0, 0, 0, 0), earlyResult);
            Assert.Equal(3, _databaseCoreManager.DatabaseCore.GetAllApiTokens().Count);

            // Past the window: the dead row is gone (durable AND live index), the live
            // rows survive untouched. The projection carries no TokenId (by design), so
            // the durable row is correlated by EntityId.
            var result = cleaner.RunOnce(realNow.AddMinutes(15));

            Assert.Equal((0, 1, 0, 0), result);
            var remaining = _databaseCoreManager.DatabaseCore.GetAllApiTokens();
            Assert.Equal(2, remaining.Count);
            Assert.Contains(remaining, r => r.Entity.EntityId == live.EntityId);
            Assert.NotNull(manager.GetTokenByEntityId(live.EntityId));
            Assert.Null(manager.GetTokenByEntityId(revoked.EntityId));
        }

        [Fact]
        public void OrphanRows_WaitOneWindowFromFirstObservation_ThenAreRemoved()
        {
            var mismatchRow = new ApiTokenEntity
            {
                EntityVersion = 1,
                EntityId = Guid.NewGuid(),
                TokenId = new string('Q', ApiTokenMaterial.TokenIdLength),
                VersionByte = ApiTokenMaterial.CurrentVersionByte,
                Verifier = new byte[32],
                OwnerUserId = OwnerId,
                Name = "key-payload-mismatch",
                Grants = [],
                CreatedAtUtc = DateTime.UtcNow.Ticks,
            };

            var orphanKey = new string('A', ApiTokenMaterial.TokenIdLength);

            // The manager observes the orphan through a damaged scan; removal goes to the
            // real database (TryRemoveToken by the storage key; an absent row is "gone").
            var failing = new FailingDatabaseCore(_databaseCoreManager.DatabaseCore, _ => false)
            {
                OverrideApiTokenScan = () => [(orphanKey, mismatchRow)],
            };

            using var manager = new ApiTokenManager(failing, NullLogger<ApiTokenManager>.Instance);
            manager.Initialize().Wait();

            var firstSeen = DateTime.UtcNow;
            var cleaner = CreateCleaner(new ApiTokensConfig { TokenRecordRetention = TimeSpan.FromMinutes(10), SecurityEventRetention = EventsPinnedOff },
                _databaseCoreManager.DatabaseCore, manager);

            // A damaged row has no trustworthy clock, so the window runs from first
            // observation: inside the window the row stays.
            var firstPass = cleaner.RunOnce(firstSeen);

            Assert.Equal((0, 0, 0, 0), firstPass);
            Assert.Contains(manager.GetOrphanTokenIds(), id => id == orphanKey);

            var secondPass = cleaner.RunOnce(firstSeen.AddMinutes(15));

            Assert.Equal((0, 0, 1, 0), secondPass);
            Assert.DoesNotContain(manager.GetOrphanTokenIds(), id => id == orphanKey);
        }

        [Fact]
        public void SecurityEvents_StrictlyOlderThanCutoffRemoved_AtCutoffAndNewerStay()
        {
            var cleaner = CreateCleaner(
                new ApiTokensConfig { SecurityEventRetention = TimeSpan.FromDays(1) },
                _databaseCoreManager.DatabaseCore, CreateManager());

            // Tests in this class share one LevelDB fixture and event rows survive across
            // tests: start from a clean event table so the exact counts below are
            // order-independent.
            DrainSecurityEvents();

            var cutoff = Now - TimeSpan.FromDays(1);

            PutSecurityEvent(Now.AddDays(-2));       // older: removed
            PutSecurityEvent(cutoff);                // exactly at the cutoff: survives
            PutSecurityEvent(Now);                   // fresh: survives

            var result = cleaner.RunOnce(Now);

            Assert.Equal((0, 0, 0, 1), result);

            var remaining = _databaseCoreManager.DatabaseCore.ReadApiTokenSecurityEvents()
                .Select(e => e.TimestampUtc)
                .ToList();

            Assert.DoesNotContain(Now.AddDays(-2).Ticks, remaining);
            Assert.Contains(cutoff.Ticks, remaining);
            Assert.Contains(Now.Ticks, remaining);
        }

        [Fact]
        public void LegacyExpiredRow_IsReapedByPassOne_ByItsOwnDeathStamp()
        {
            // #1384 keeps the expiry half of DeadAtTicks deliberately: a
            // pre-simplification row (rejected at load, so absent from the live index)
            // with an already-old expiry is reaped by pass 1 immediately, instead of
            // waiting the orphan pass's first-observation window. Future expiry —
            // before the retention cutoff elapses — keeps the row for the window, like
            // any other death stamp.
            var realNow = DateTime.UtcNow;
            var retention = TimeSpan.FromMinutes(10);

            var longDead = new string('D', ApiTokenMaterial.TokenIdLength);
            var recentlyDead = new string('E', ApiTokenMaterial.TokenIdLength);

            _databaseCoreManager.DatabaseCore.PutApiToken(new HSMDatabase.AccessManager.DatabaseEntities.ApiTokenEntity
            {
                EntityVersion = 1,
                EntityId = Guid.NewGuid(),
                TokenId = longDead,
                VersionByte = ApiTokenMaterial.CurrentVersionByte,
                Verifier = new byte[32],
                OwnerUserId = OwnerId,
                Name = "legacy-expired",
                Grants = [],
                CreatedAtUtc = DateTime.UtcNow.AddDays(-40).Ticks,
                ExpiresAtUtc = (realNow - TimeSpan.FromDays(30)).Ticks,
            });

            _databaseCoreManager.DatabaseCore.PutApiToken(new HSMDatabase.AccessManager.DatabaseEntities.ApiTokenEntity
            {
                EntityVersion = 1,
                EntityId = Guid.NewGuid(),
                TokenId = recentlyDead,
                VersionByte = ApiTokenMaterial.CurrentVersionByte,
                Verifier = new byte[32],
                OwnerUserId = OwnerId,
                Name = "legacy-expiring-later",
                Grants = [],
                CreatedAtUtc = DateTime.UtcNow.AddDays(-40).Ticks,
                ExpiresAtUtc = (realNow + TimeSpan.FromMinutes(5)).Ticks,
            });

            using var manager = CreateManager();
            manager.Initialize().Wait();

            var cleaner = CreateCleaner(new ApiTokensConfig { TokenRecordRetention = retention, SecurityEventRetention = EventsPinnedOff },
                _databaseCoreManager.DatabaseCore, manager);

            var result = cleaner.RunOnce(realNow);

            Assert.Equal(1, result.TokenRowsRemoved);
            Assert.Null(_databaseCoreManager.DatabaseCore.GetApiToken(longDead));
            Assert.NotNull(_databaseCoreManager.DatabaseCore.GetApiToken(recentlyDead));
        }

        [Fact]
        public void DeadRowExactlyAtTheCutoff_IsRemoved_InclusiveBoundary()
        {
            // The token-row cutoff is INCLUSIVE (deadAt <= cutoff), unlike the
            // security-event cutoff (strictly older). Pinned at the exact boundary.
            using var manager = CreateManager();
            manager.Initialize().Wait();

            var retention = TimeSpan.FromMinutes(10);
            var cleaner = CreateCleaner(new ApiTokensConfig { TokenRecordRetention = retention, SecurityEventRetention = EventsPinnedOff },
                _databaseCoreManager.DatabaseCore, manager);

            Assert.True(manager.TryCreateToken(OwnerId, "boundary", readOnly: false, "test", out var token, out _));
            Assert.True(manager.TryRevokeToken(token.EntityId, "test", "boundary test", out _));

            // Bit-exact death stamp read back from the durable row: a DateTime.UtcNow
            // captured AFTER the revoke is strictly newer than the stored stamp, so it
            // would never pin the inclusive boundary this test exists for.
            var revokedAt = new DateTime(
                _databaseCoreManager.DatabaseCore.GetAllApiTokens()
                    .Single(r => r.Entity.EntityId == token.EntityId).Entity.RevokedAtUtc.Value,
                DateTimeKind.Utc);

            // One tick before the window elapses: the cutoff is still older than the
            // revoke stamp — kept.
            Assert.Equal((0, 0, 0, 0), cleaner.RunOnce(revokedAt + retention - TimeSpan.FromTicks(1)));

            // Exactly at the window: cutoff == RevokedAtUtc — removed (inclusive).
            Assert.Equal((0, 1, 0, 0), cleaner.RunOnce(revokedAt + retention));
        }

        [Fact]
        public void EmergencyRevokedRows_AreStamped_ThenRemovedAfterTheWindow()
        {
            // An emergency revoke advances the generation and never touches the rows:
            // the sweep's reconciliation pass is what eventually retires them. The
            // stamp carries the sweep's observation clock and the coarse "emergency"
            // actor — the initiator, reason and exact revoke instant live in the
            // journal's audit record, not on every row.
            using var manager = CreateManager();
            manager.Initialize().Wait();

            var retention = TimeSpan.FromMinutes(10);
            var cleaner = CreateCleaner(new ApiTokensConfig { TokenRecordRetention = retention, SecurityEventRetention = EventsPinnedOff },
                _databaseCoreManager.DatabaseCore, manager);

            Assert.True(manager.TryCreateToken(OwnerId, "emergency-killed", readOnly: false, "test", out var token, out _));

            // The public projection carries no TokenId (by design); the storage key IS
            // the token id, so IsTokenLive is driven from the durable row.
            var keyTokenId = _databaseCoreManager.DatabaseCore.GetAllApiTokens()
                .Single(r => r.Entity.EntityId == token.EntityId).KeyTokenId;

            manager.AdvanceOwnerRevocationGeneration(OwnerId);

            // The token is dead to authentication the moment the generation advanced...
            Assert.False(manager.IsTokenLive(keyTokenId));

            // ...but the row keeps RevokedAtUtc null until the sweep stamps it.
            var stampedPass = cleaner.RunOnce(DateTime.UtcNow);

            Assert.Equal((1, 0, 0, 0), stampedPass);

            var row = _databaseCoreManager.DatabaseCore.GetAllApiTokens()
                .Single(r => r.Entity.EntityId == token.EntityId).Entity;

            Assert.NotNull(row.RevokedAtUtc);
            Assert.Equal(ApiTokenManager.EmergencyRevokedBy, row.RevokedBy);
            Assert.Null(row.RevocationReason);

            // A freshly stamped row is never removed by the pass that stamped it: the
            // retention window counts from the (just now) stamp. Bit-exact readback,
            // like the inclusive-boundary test — an after-the-fact UtcNow is strictly
            // newer and could never pin the removal boundary.
            var stampedAt = new DateTime(row.RevokedAtUtc.Value, DateTimeKind.Utc);

            Assert.Equal((0, 0, 0, 0), cleaner.RunOnce(stampedAt + retention - TimeSpan.FromTicks(1)));
            Assert.Equal((0, 1, 0, 0), cleaner.RunOnce(stampedAt + retention));
        }

        [Fact]
        public void EmergencyRevocationStamping_IsBounded_TheRestDrainsNextPass()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            var cleaner = CreateCleaner(new ApiTokensConfig { TokenRecordRetention = TimeSpan.FromMinutes(10), SecurityEventRetention = EventsPinnedOff },
                _databaseCoreManager.DatabaseCore, manager);

            // One more invalidated row than the per-pass batch limit (the internal test
            // ctor's 0 = unlimited quota admits creating them all).
            var total = ApiTokenRetentionCleaner.TokenRowBatchLimit + 1;

            for (var i = 0; i < total; i++)
                Assert.True(manager.TryCreateToken(OwnerId, $"killed-{i}", readOnly: false, "test", out _, out _),
                    $"token {i} must be created");

            manager.AdvanceGlobalRevocationGeneration();

            var firstPass = cleaner.RunOnce(DateTime.UtcNow);

            Assert.Equal(ApiTokenRetentionCleaner.TokenRowBatchLimit, firstPass.InvalidatedRowsStamped);
            Assert.Equal(total, _databaseCoreManager.DatabaseCore.GetAllApiTokens().Count);

            var secondPass = cleaner.RunOnce(DateTime.UtcNow);

            Assert.Equal(1, secondPass.InvalidatedRowsStamped);
        }

        [Fact]
        public void Stamping_NeverTouches_LiveOrPersonallyRevokedRows()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            var cleaner = CreateCleaner(new ApiTokensConfig { TokenRecordRetention = TimeSpan.FromMinutes(10), SecurityEventRetention = EventsPinnedOff },
                _databaseCoreManager.DatabaseCore, manager);

            // The live row belongs to ANOTHER owner: an owner-generation advance kills
            // every token that owner issued earlier, so a same-owner "live" witness
            // would be invalidated together with the target.
            var untouchedOwner = Guid.NewGuid();

            Assert.True(manager.TryCreateToken(untouchedOwner, "live", readOnly: false, "test", out var live, out _));
            Assert.True(manager.TryCreateToken(OwnerId, "revoked", readOnly: false, "test", out var revoked, out _));
            Assert.True(manager.TryRevokeToken(revoked.EntityId, "test", "personal revoke", out _));
            Assert.True(manager.TryCreateToken(OwnerId, "invalidated", readOnly: false, "test", out var invalidated, out _));

            manager.AdvanceOwnerRevocationGeneration(OwnerId);

            var result = cleaner.RunOnce(DateTime.UtcNow);

            Assert.Equal(1, result.InvalidatedRowsStamped);

            var durableRows = _databaseCoreManager.DatabaseCore.GetAllApiTokens().ToList();
            var rows = durableRows.ToDictionary(r => r.Entity.EntityId, r => r.Entity);
            var keys = durableRows.ToDictionary(r => r.Entity.EntityId, r => r.KeyTokenId);

            Assert.Null(rows[live.EntityId].RevokedAtUtc);
            Assert.Equal("test", rows[revoked.EntityId].RevokedBy);
            Assert.Equal("personal revoke", rows[revoked.EntityId].RevocationReason);
            Assert.Equal(ApiTokenManager.EmergencyRevokedBy, rows[invalidated.EntityId].RevokedBy);

            // The live row is still a live credential; the invalidated one is not.
            Assert.True(manager.IsTokenLive(keys[live.EntityId]));
            Assert.False(manager.IsTokenLive(keys[invalidated.EntityId]));
        }

        [Fact]
        public void EmergencyRevocationStamping_RefusesWhileGenerationStateIsUnhealthy()
        {
            // The reachable degraded shape the stamping pass must refuse: the token
            // scan LOADED the index while the generation read failed, so the in-memory
            // counters sit at zero and every loaded row looks invalidated. A pass that
            // stamped from those values would irreversibly revoke every live token
            // before the operator repairs the storage and restarts — the same reason
            // minting refuses unproven generations.
            using var healthy = CreateManager();
            healthy.Initialize().Wait();

            Assert.True(healthy.TryCreateToken(OwnerId, "must-survive", readOnly: false, "test", out var token, out _));

            var failing = new FailingDatabaseCore(_databaseCoreManager.DatabaseCore, _ => false)
            {
                ShouldFailApiTokenOp = op => op == nameof(FailingDatabaseCore.GetGlobalRevocationGeneration),
            };

            using var unhealthy = new ApiTokenManager(failing, NullLogger<ApiTokenManager>.Instance);
            unhealthy.Initialize().Wait();

            // Index populated (the scan succeeded), generation state unproven.
            Assert.False(unhealthy.IsGenerationStateHealthy);
            Assert.NotNull(unhealthy.GetTokenByEntityId(token.EntityId));

            var cleaner = CreateCleaner(new ApiTokensConfig { TokenRecordRetention = TimeSpan.Zero, SecurityEventRetention = EventsPinnedOff },
                _databaseCoreManager.DatabaseCore, unhealthy);

            var result = cleaner.RunOnce(DateTime.UtcNow);

            Assert.Equal((0, 0, 0, 0), result);

            var row = _databaseCoreManager.DatabaseCore.GetAllApiTokens()
                .Single(r => r.Entity.EntityId == token.EntityId).Entity;

            // No stamp, no removal: the row survives verbatim for the healthy boot
            // that follows the storage repair.
            Assert.Null(row.RevokedAtUtc);
            Assert.Null(row.RevokedBy);
        }

        [Fact]
        public void StorageFailure_Isolated_PerPass_NeverThrows()
        {
            var failing = new FailingDatabaseCore(_databaseCoreManager.DatabaseCore, _ => false)
            {
                ShouldFailApiTokenOp = op => op == nameof(FailingDatabaseCore.GetAllApiTokens),
            };

            // The orphan pass THROWS (a storage failure under TryRemoveToken, with
            // TokenRecordRetention = 0 so the first-observation gate is already elapsed
            // and the removal is actually attempted): it must be isolated like the other
            // passes, and the security-event pass behind it must still run and remove an
            // eligible event.
            var manager = new Mock<IApiTokenManager>();
            manager.Setup(m => m.GetOrphanTokenIds()).Returns(new[] { new string('A', ApiTokenMaterial.TokenIdLength) });
            manager.Setup(m => m.TryRemoveToken(It.IsAny<string>()))
                .Throws(new InvalidOperationException("simulated orphan removal failure"));

            DrainSecurityEvents();
            PutSecurityEvent(Now.AddDays(-2));

            var cleaner = new ApiTokenRetentionCleaner(failing, manager.Object,
                new ApiTokensConfig { TokenRecordRetention = TimeSpan.Zero, SecurityEventRetention = TimeSpan.FromDays(1) },
                NullLogger<ApiTokenRetentionCleaner>.Instance);

            var result = cleaner.RunOnce(Now);

            Assert.Equal((0, 0, 0, 1), result);
        }

        [Fact]
        public void SecurityEventBacklog_DrainsInRepeatedBatches_WithinOnePass()
        {
            // The single database delete is batch-bounded; a FULL batch means more
            // eligible rows remain, so the pass repeats until a short batch (the
            // interface's documented contract) — a backlog larger than one batch still
            // drains in one pass.
            var db = new Mock<HSMServer.Core.DataLayer.IDatabaseCore>();
            db.SetupSequence(d => d.RemoveApiTokenSecurityEventsBefore(It.IsAny<long>(), It.IsAny<int>()))
                .Returns(ApiTokenRetentionCleaner.SecurityEventBatchLimit)
                .Returns(ApiTokenRetentionCleaner.SecurityEventBatchLimit)
                .Returns(17);

            var cleaner = new ApiTokenRetentionCleaner(db.Object, new Mock<IApiTokenManager>().Object,
                new ApiTokensConfig { SecurityEventRetention = TimeSpan.FromDays(1) },
                NullLogger<ApiTokenRetentionCleaner>.Instance);

            var result = cleaner.RunOnce(Now);

            Assert.Equal(2 * ApiTokenRetentionCleaner.SecurityEventBatchLimit + 17, result.SecurityEventsRemoved);
            db.Verify(d => d.RemoveApiTokenSecurityEventsBefore(It.IsAny<long>(), It.IsAny<int>()), Times.Exactly(3));
        }

        [Fact]
        public void SecurityEventBacklog_IsCappedPerPass_TheRestDrainsNextPass()
        {
            // Every batch comes back full (an effectively unbounded backlog): one pass
            // still stops at the per-pass cap instead of sweeping forever.
            var db = new Mock<HSMServer.Core.DataLayer.IDatabaseCore>();
            db.Setup(d => d.RemoveApiTokenSecurityEventsBefore(It.IsAny<long>(), It.IsAny<int>()))
                .Returns(ApiTokenRetentionCleaner.SecurityEventBatchLimit);

            var cleaner = new ApiTokenRetentionCleaner(db.Object, new Mock<IApiTokenManager>().Object,
                new ApiTokensConfig { SecurityEventRetention = TimeSpan.FromDays(1) },
                NullLogger<ApiTokenRetentionCleaner>.Instance);

            var result = cleaner.RunOnce(Now);

            Assert.Equal(
                ApiTokenRetentionCleaner.MaxSecurityEventBatchesPerPass * (long)ApiTokenRetentionCleaner.SecurityEventBatchLimit,
                result.SecurityEventsRemoved);
            db.Verify(d => d.RemoveApiTokenSecurityEventsBefore(It.IsAny<long>(), It.IsAny<int>()),
                Times.Exactly(ApiTokenRetentionCleaner.MaxSecurityEventBatchesPerPass));
        }

        [Theory]
        [InlineData(-1)]    // negative window
        [InlineData(4000)]  // above the upper bound: utcNow - retention must not underflow DateTime
        public void Constructor_InvalidRetention_ThrowsWithTheConfigName(int retentionDays)
        {
            var ex = Assert.Throws<InvalidOperationException>(() => CreateCleaner(
                new ApiTokensConfig { TokenRecordRetention = TimeSpan.FromDays(retentionDays) },
                _databaseCoreManager.DatabaseCore, CreateManager()));

            Assert.Contains(nameof(ApiTokensConfig.TokenRecordRetention), ex.Message);
        }


        private void PutSecurityEvent(DateTime timestampUtc) =>
            _databaseCoreManager.DatabaseCore.PutApiTokenSecurityEvent(new ApiTokenSecurityEventEntity
            {
                Kind = (byte)ApiTokenSecurityEventKind.AuthFailed,
                TimestampUtc = timestampUtc.Ticks,
            });

        // Tests in this class share one LevelDB fixture (the class fixture deletes the
        // folder once), and event rows survive across tests: drain the event table before
        // any test that asserts exact event counts. long.MaxValue sorts above every real
        // event key bytewise (current-era tick strings start with '6' < '9').
        private void DrainSecurityEvents() =>
            _databaseCoreManager.DatabaseCore.RemoveApiTokenSecurityEventsBefore(long.MaxValue, int.MaxValue);


        public class Fixture : DatabaseFixture
        {
            protected override string DatabaseFolder => nameof(ApiTokenRetentionCleanerTests);
        }
    }
}
