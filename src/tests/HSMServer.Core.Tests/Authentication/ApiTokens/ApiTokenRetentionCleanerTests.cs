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
        private static readonly DateTime Now = new(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc);


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
            var cleaner = CreateCleaner(new ApiTokensConfig { TokenRecordRetention = retention },
                _databaseCoreManager.DatabaseCore, manager);

            var realNow = DateTime.UtcNow;

            Assert.True(manager.TryCreateToken(OwnerId, "live", null, [], expiresAtUtc: null, "test", out var live, out _));
            Assert.True(manager.TryCreateToken(OwnerId, "revoked", null, [], expiresAtUtc: null, "test", out var revoked, out _));
            Assert.True(manager.TryRevokeToken(revoked.EntityId, "test", "cleanup test", out _));
            Assert.True(manager.TryCreateToken(OwnerId, "expired", null, [], expiresAtUtc: realNow.AddMinutes(1), "test", out var expired, out _));

            // Halfway through the window: nothing is eligible yet.
            var earlyResult = cleaner.RunOnce(realNow.AddMinutes(5));

            Assert.Equal((0, 0, 0), earlyResult);
            Assert.Equal(3, _databaseCoreManager.DatabaseCore.GetAllApiTokens().Count);

            // Past the window: the dead rows are gone (durable AND live index), the live
            // row survives untouched. The projection carries no TokenId (by design), so
            // the durable row is correlated by EntityId.
            var result = cleaner.RunOnce(realNow.AddMinutes(15));

            Assert.Equal((2, 0, 0), result);
            var remaining = _databaseCoreManager.DatabaseCore.GetAllApiTokens();
            Assert.Single(remaining);
            Assert.Equal(live.EntityId, remaining[0].Entity.EntityId);
            Assert.NotNull(manager.GetTokenByEntityId(live.EntityId));
            Assert.Null(manager.GetTokenByEntityId(revoked.EntityId));
            Assert.Null(manager.GetTokenByEntityId(expired.EntityId));
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
            var cleaner = CreateCleaner(new ApiTokensConfig { TokenRecordRetention = TimeSpan.FromMinutes(10) },
                _databaseCoreManager.DatabaseCore, manager);

            // A damaged row has no trustworthy clock, so the window runs from first
            // observation: inside the window the row stays.
            var firstPass = cleaner.RunOnce(firstSeen);

            Assert.Equal((0, 0, 0), firstPass);
            Assert.Contains(manager.GetOrphanTokenIds(), id => id == orphanKey);

            var secondPass = cleaner.RunOnce(firstSeen.AddMinutes(15));

            Assert.Equal((0, 1, 0), secondPass);
            Assert.DoesNotContain(manager.GetOrphanTokenIds(), id => id == orphanKey);
        }

        [Fact]
        public void SecurityEvents_StrictlyOlderThanCutoffRemoved_AtCutoffAndNewerStay()
        {
            var cleaner = CreateCleaner(
                new ApiTokensConfig { SecurityEventRetention = TimeSpan.FromDays(1) },
                _databaseCoreManager.DatabaseCore, CreateManager());

            var cutoff = Now - TimeSpan.FromDays(1);

            PutSecurityEvent(Now.AddDays(-2));       // older: removed
            PutSecurityEvent(cutoff);                // exactly at the cutoff: survives
            PutSecurityEvent(Now);                   // fresh: survives

            var result = cleaner.RunOnce(Now);

            Assert.Equal((0, 0, 1), result);

            var remaining = _databaseCoreManager.DatabaseCore.ReadApiTokenSecurityEvents()
                .Select(e => e.TimestampUtc)
                .ToList();

            Assert.DoesNotContain(Now.AddDays(-2).Ticks, remaining);
            Assert.Contains(cutoff.Ticks, remaining);
            Assert.Contains(Now.Ticks, remaining);
        }

        [Fact]
        public void DeadRowExactlyAtTheCutoff_IsRemoved_InclusiveBoundary()
        {
            // The token-row cutoff is INCLUSIVE (deadAt <= cutoff), unlike the
            // security-event cutoff (strictly older). Pinned at the exact boundary.
            using var manager = CreateManager();
            manager.Initialize().Wait();

            var retention = TimeSpan.FromMinutes(10);
            var cleaner = CreateCleaner(new ApiTokensConfig { TokenRecordRetention = retention },
                _databaseCoreManager.DatabaseCore, manager);

            Assert.True(manager.TryCreateToken(OwnerId, "boundary", null, [], expiresAtUtc: null, "test", out var token, out _));
            Assert.True(manager.TryRevokeToken(token.EntityId, "test", "boundary test", out _));
            var revokedAt = DateTime.UtcNow;

            // One tick before the window elapses: the cutoff is still older than the
            // revoke stamp — kept.
            Assert.Equal((0, 0, 0), cleaner.RunOnce(revokedAt + retention - TimeSpan.FromSeconds(1)));

            // Exactly at the window: cutoff == RevokedAtUtc — removed (inclusive).
            Assert.Equal((1, 0, 0), cleaner.RunOnce(revokedAt + retention));
        }

        [Fact]
        public void StorageFailure_Isolated_PerPass_NeverThrows()
        {
            var failing = new FailingDatabaseCore(_databaseCoreManager.DatabaseCore, _ => false)
            {
                ShouldFailApiTokenOp = op => op == nameof(FailingDatabaseCore.GetAllApiTokens),
            };

            // The orphan pass THROWS (a storage failure under TryRemoveToken): it must be
            // isolated like the other passes, and the security-event pass behind it must
            // still run and remove an eligible event.
            var manager = new Mock<IApiTokenManager>();
            manager.Setup(m => m.GetOrphanTokenIds()).Returns(new[] { new string('A', ApiTokenMaterial.TokenIdLength) });
            manager.Setup(m => m.TryRemoveToken(It.IsAny<string>()))
                .Throws(new InvalidOperationException("simulated orphan removal failure"));

            PutSecurityEvent(Now.AddDays(-2));

            var cleaner = new ApiTokenRetentionCleaner(failing, manager.Object,
                new ApiTokensConfig { SecurityEventRetention = TimeSpan.FromDays(1) },
                NullLogger<ApiTokenRetentionCleaner>.Instance);

            var result = cleaner.RunOnce(Now);

            Assert.Equal((0, 0, 1), result);
        }

        [Fact]
        public void Constructor_InvalidRetention_ThrowsWithTheConfigName()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => CreateCleaner(
                new ApiTokensConfig { TokenRecordRetention = TimeSpan.FromMinutes(-1) },
                _databaseCoreManager.DatabaseCore, CreateManager()));

            Assert.Contains(nameof(ApiTokensConfig.TokenRecordRetention), ex.Message);
        }


        private void PutSecurityEvent(DateTime timestampUtc) =>
            _databaseCoreManager.DatabaseCore.PutApiTokenSecurityEvent(new ApiTokenSecurityEventEntity
            {
                Kind = (byte)ApiTokenSecurityEventKind.AuthFailed,
                TimestampUtc = timestampUtc.Ticks,
            });


        public class Fixture : DatabaseFixture
        {
            protected override string DatabaseFolder => nameof(ApiTokenRetentionCleanerTests);
        }
    }
}
