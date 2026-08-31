using System;
using System.Collections.Generic;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.Core.Tests.DatabaseTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HSMServer.Core.Tests.Authentication.ApiTokens
{
    // Lifecycle contract of the authoritative token index: persist-first publication, one-time
    // secret disclosure, non-expanding restriction/rotation, idempotent revocation, durable
    // revocation generations, and quota counting semantics.
    [Collection("Database collection")]
    public class ApiTokenManagerTests : DatabaseCoreTestsBase<ApiTokenManagerFixture>, IClassFixture<DatabaseRegisterFixture>
    {
        private static readonly Guid OwnerId = Guid.NewGuid();


        public ApiTokenManagerTests(ApiTokenManagerFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture) { }


        private ApiTokenManager CreateManager() =>
            new(_databaseCoreManager.DatabaseCore, NullLogger<ApiTokenManager>.Instance);


        [Fact]
        public void Initialize_FreshDatabase_IsHealthyWithZeroGenerations()
        {
            using var manager = CreateManager();

            manager.Initialize().Wait();

            Assert.True(manager.IsGenerationStateHealthy);
            Assert.Equal(0, manager.GlobalRevocationGeneration);
            Assert.Equal(0, manager.GetOwnerRevocationGeneration(OwnerId));
        }

        [Fact]
        public void TryCreateToken_PersistsFirst_SecretDisclosedOnce()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            var created = manager.TryCreateToken(
                OwnerId, "monitoring", "read-only monitoring",
                BuildGrants("alerts:read"), expiresAtUtc: DateTime.UtcNow.AddDays(30),
                createdBy: "test-user", out var entity, out var fullToken);

            Assert.True(created);
            Assert.NotNull(entity);
            Assert.StartsWith("hsm_pat_v1_", fullToken);
            Assert.True(ApiTokenMaterial.TryParse(fullToken, out var versionByte, out var tokenIdBytes, out _));
            Assert.Equal(ApiTokenMaterial.CurrentVersionByte, versionByte);

            // The stored verifier matches the presented secret, but no stored field equals
            // the secret itself.
            var expectedVerifier = ApiTokenVerifier.ComputeVerifier(
                ApiTokenMaterial.CurrentVersionByte, tokenIdBytes,
                Convert.FromBase64String(Base64UrlToBase64(SecretPart(fullToken))));

            Assert.Equal(expectedVerifier, entity.Verifier);
            Assert.Equal(entity.TokenId, manager.GetToken(entity.TokenId).TokenId);
            Assert.Equal(entity.EntityId, manager.GetTokenByEntityId(entity.EntityId).EntityId);
            Assert.Single(manager.GetTokensByOwner(OwnerId), token => token.EntityId == entity.EntityId);
        }

        [Fact]
        public void TryCreateToken_SurvivesManagerRestart()
        {
            ApiTokenEntity entity;

            using (var manager = CreateManager())
            {
                manager.Initialize().Wait();

                manager.TryCreateToken(OwnerId, "restart-proof", null, BuildGrants("products:read"),
                    null, "test-user", out entity, out _);
            }

            using var reopened = CreateManager();
            reopened.Initialize().Wait();

            var reloaded = reopened.GetToken(entity.TokenId);

            Assert.NotNull(reloaded);
            Assert.Equal(entity.EntityId, reloaded.EntityId);
            Assert.Equal(entity.Grants.Count, reloaded.Grants.Count);
            Assert.Equal(entity.Verifier, reloaded.Verifier);
        }

        [Fact]
        public void TryCreateToken_RejectsBadInput()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            Assert.False(manager.TryCreateToken(Guid.Empty, "no owner", null, BuildGrants("alerts:read"), null, "u", out _, out _));
            Assert.False(manager.TryCreateToken(OwnerId, "  ", null, BuildGrants("alerts:read"), null, "u", out _, out _));
            Assert.False(manager.TryCreateToken(OwnerId, "bad grants", null,
                [new ApiTokenGrantEntity { Operation = "nonsense:read", BoundaryKind = (byte)ApiTokenBoundaryKind.Global }], null, "u", out _, out _));
            Assert.False(manager.TryCreateToken(OwnerId, "past expiry", null, BuildGrants("alerts:read"),
                DateTime.UtcNow.AddDays(-1), "u", out _, out _));
        }

        [Fact]
        public void TryCreateToken_ManyTokens_AllUniqueAndQuotaCounted()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            var tokenIds = new HashSet<string>();

            for (var i = 0; i < 50; i++)
            {
                Assert.True(manager.TryCreateToken(OwnerId, $"token-{i}", null, BuildGrants("alerts:read"), null, "u", out var entity, out _));
                tokenIds.Add(entity.TokenId);
            }

            Assert.Equal(50, tokenIds.Count);
            Assert.Equal(50, manager.CountQuotaEligibleTokens(OwnerId));
        }

        [Fact]
        public void TryRevokeToken_IsImmediateAndIdempotent()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            manager.TryCreateToken(OwnerId, "to-revoke", null, BuildGrants("alerts:read"), null, "u", out var entity, out _);

            Assert.True(manager.TryRevokeToken(entity.EntityId, "test-user", "rotation cleanup", out var revoked));
            Assert.NotNull(revoked.RevokedAtUtc);

            var firstRevokedAt = revoked.RevokedAtUtc;

            Assert.True(manager.TryRevokeToken(entity.EntityId, "test-user", "again", out var again));
            Assert.Equal(firstRevokedAt, again.RevokedAtUtc);

            // Revoked tokens never count toward the quota.
            Assert.Equal(0, manager.CountQuotaEligibleTokens(OwnerId));
        }

        [Fact]
        public void TryRestrictToken_RemovesGrantsAndShortensExpiry()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            manager.TryCreateToken(OwnerId, "to-restrict", null, BuildGrants("alerts:read", "alerts:write"),
                DateTime.UtcNow.AddYears(1), "u", out var entity, out _);

            var shorterExpiry = DateTime.UtcNow.AddDays(1);

            Assert.True(manager.TryRestrictToken(entity.EntityId, BuildGrants("alerts:read"), shorterExpiry, "u", out var restricted));

            Assert.Single(restricted.Grants);
            Assert.Equal("alerts:read", restricted.Grants[0].Operation);
            Assert.NotNull(restricted.RestrictedAtUtc);
            Assert.Equal(shorterExpiry.ToUniversalTime().Ticks, restricted.ExpiresAtUtc.Value);

            // Persisted: a fresh index sees the restricted state.
            using var reopened = CreateManager();
            reopened.Initialize().Wait();

            var reloaded = reopened.GetToken(entity.TokenId);

            Assert.Single(reloaded.Grants);
            Assert.NotNull(reloaded.RestrictedAtUtc);
        }

        [Fact]
        public void TryRestrictToken_RejectsExpansionAndExpiryExtension()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            var expiry = DateTime.UtcNow.AddDays(10);

            manager.TryCreateToken(OwnerId, "no-expand", null, BuildGrants("alerts:read", "alerts:write"), expiry, "u", out var entity, out _);

            // A pair the token never had.
            Assert.False(manager.TryRestrictToken(entity.EntityId, BuildGrants("sensors:read"), null, "u", out _));

            // A boundary the token never had.
            Assert.False(manager.TryRestrictToken(entity.EntityId,
                [new ApiTokenGrantEntity { Operation = "alerts:read", BoundaryKind = (byte)ApiTokenBoundaryKind.Product, BoundaryId = Guid.NewGuid().ToString() }],
                null, "u", out _));

            // Extending a finite expiry.
            Assert.False(manager.TryRestrictToken(entity.EntityId, BuildGrants("alerts:read"), expiry.AddDays(1), "u", out _));

            // The token is unchanged after the failed attempts.
            var unchanged = manager.GetToken(entity.TokenId);

            Assert.Equal(2, unchanged.Grants.Count);
            Assert.Equal(expiry.ToUniversalTime().Ticks, unchanged.ExpiresAtUtc.Value);
            Assert.Null(unchanged.RestrictedAtUtc);
        }

        [Fact]
        public void TryRestrictToken_UnlimitedMayBecomeFinite()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            manager.TryCreateToken(OwnerId, "unlimited", null, BuildGrants("alerts:read"), null, "u", out var entity, out _);

            var finite = DateTime.UtcNow.AddMonths(6);

            Assert.True(manager.TryRestrictToken(entity.EntityId, BuildGrants("alerts:read"), finite, "u", out var restricted));
            Assert.Equal(finite.ToUniversalTime().Ticks, restricted.ExpiresAtUtc.Value);
        }

        [Fact]
        public void TryRotateToken_RevokesOldIssuesFreshPairAndPreservesGrants()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            var expiry = DateTime.UtcNow.AddMonths(3);

            manager.TryCreateToken(OwnerId, "to-rotate", "desc", BuildGrants("alerts:read", "alerts:write"), expiry, "u", out var old, out var oldFullToken);

            Assert.True(manager.TryRotateToken(old.EntityId, null, "rotating-user", out var replacement, out var newFullToken));

            // Completely fresh identifiers: no value from the old token is reused.
            Assert.NotEqual(old.EntityId, replacement.EntityId);
            Assert.NotEqual(old.TokenId, replacement.TokenId);
            Assert.NotEqual(oldFullToken, newFullToken);
            Assert.Equal(old.EntityId, replacement.RotatedFromEntityId);
            Assert.NotNull(replacement.RotatedAtUtc);

            // Grants and expiry preserved, not expanded.
            Assert.Equal(old.Grants.Count, replacement.Grants.Count);
            Assert.Equal(old.ExpiresAtUtc, replacement.ExpiresAtUtc);

            // Old token is revoked immediately, new one authenticates on lookup.
            Assert.NotNull(manager.GetToken(old.TokenId).RevokedAtUtc);
            Assert.Null(manager.GetToken(replacement.TokenId).RevokedAtUtc);

            // The replacement takes the source slot: still one quota-eligible token.
            Assert.Equal(1, manager.CountQuotaEligibleTokens(OwnerId));
        }

        [Fact]
        public void TryRotateToken_CannotMakeFiniteExpiryUnlimitedOrExtendIt()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            var expiry = DateTime.UtcNow.AddDays(10);

            manager.TryCreateToken(OwnerId, "finite", null, BuildGrants("alerts:read"), expiry, "u", out var entity, out _);

            Assert.False(manager.TryRotateToken(entity.EntityId, expiry.AddDays(5), "u", out _, out _));

            // Rotating without a requested expiry keeps the finite value (never unlimited).
            Assert.True(manager.TryRotateToken(entity.EntityId, null, "u", out var replacement, out _));
            Assert.Equal(expiry.ToUniversalTime().Ticks, replacement.ExpiresAtUtc.Value);
        }

        [Fact]
        public void AdvanceGlobalRevocationGeneration_InvalidatesEveryTokenForQuotaImmediately()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            manager.TryCreateToken(OwnerId, "one", null, BuildGrants("alerts:read"), null, "u", out _, out _);
            manager.TryCreateToken(OwnerId, "two", null, BuildGrants("alerts:read"), null, "u", out _, out _);

            Assert.Equal(2, manager.CountQuotaEligibleTokens(OwnerId));

            Assert.Equal(1, manager.AdvanceGlobalRevocationGeneration());

            // Records still exist and are individually active, but generation-invalidated
            // tokens stop counting immediately — before any per-record reconciliation.
            Assert.Equal(0, manager.CountQuotaEligibleTokens(OwnerId));
            Assert.Equal(2, manager.GetTokensByOwner(OwnerId).Count);
        }

        [Fact]
        public void AdvanceOwnerRevocationGeneration_InvalidatesOnlyThatOwner()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            var otherOwner = Guid.NewGuid();

            manager.TryCreateToken(OwnerId, "mine", null, BuildGrants("alerts:read"), null, "u", out _, out _);
            manager.TryCreateToken(otherOwner, "theirs", null, BuildGrants("alerts:read"), null, "u", out _, out _);

            Assert.Equal(1, manager.AdvanceOwnerRevocationGeneration(OwnerId));

            Assert.Equal(0, manager.CountQuotaEligibleTokens(OwnerId));
            Assert.Equal(1, manager.CountQuotaEligibleTokens(otherOwner));
        }

        [Fact]
        public void Initialize_RegressedGenerationState_FailsClosed()
        {
            // A record issued at a generation newer than the authoritative one can only mean
            // damaged generation storage: the whole index must fail closed.
            _databaseCoreManager.DatabaseCore.PutApiToken(new ApiTokenEntity
            {
                EntityVersion = 1,
                EntityId = Guid.NewGuid(),
                TokenId = new string('A', ApiTokenMaterial.TokenIdLength),
                VersionByte = ApiTokenMaterial.CurrentVersionByte,
                Verifier = new byte[32],
                OwnerUserId = OwnerId,
                GlobalRevocationGenerationAtIssue = 5,
                OwnerRevocationGenerationAtIssue = 0,
                Name = "from-the-future",
                Grants = BuildGrants("alerts:read"),
                CreatedAtUtc = DateTime.UtcNow.Ticks,
            });

            using var manager = CreateManager();

            manager.Initialize().Wait();

            Assert.False(manager.IsGenerationStateHealthy);
        }

        [Fact]
        public void Initialize_UnloadableRecord_IsSkippedAndNeverAuthenticates()
        {
            // Wrong TokenId shape: cannot be a valid bearer credential, so it must not be
            // published to the authentication index at all.
            _databaseCoreManager.DatabaseCore.PutApiToken(new ApiTokenEntity
            {
                EntityVersion = 1,
                EntityId = Guid.NewGuid(),
                TokenId = "short",
                VersionByte = ApiTokenMaterial.CurrentVersionByte,
                Verifier = new byte[32],
                OwnerUserId = OwnerId,
                Name = "corrupt",
                Grants = BuildGrants("alerts:read"),
                CreatedAtUtc = DateTime.UtcNow.Ticks,
            });

            using var manager = CreateManager();

            manager.Initialize().Wait();

            Assert.Null(manager.GetToken("short"));
            Assert.True(manager.IsGenerationStateHealthy);
        }


        [Fact]
        public void TryCreateToken_WriteFailure_LeavesNeitherDurableNorLiveState()
        {
            var failing = new HSMServer.Core.Tests.Infrastructure.FailingDatabaseCore(_databaseCoreManager.DatabaseCore, _ => false)
            {
                ShouldFailApiTokenOp = op => op == "TryInsertApiToken",
            };

            using var manager = new ApiTokenManager(failing, NullLogger<ApiTokenManager>.Instance);
            manager.Initialize().Wait();

            Assert.False(manager.TryCreateToken(OwnerId, "doomed", null, BuildGrants("alerts:read"), null, "u", out _, out _));
            Assert.Empty(manager.GetTokensByOwner(OwnerId));

            // Nothing reached the durable store either: a fresh index sees no tokens.
            using var reopened = CreateManager();
            reopened.Initialize().Wait();

            Assert.Empty(reopened.GetTokensByOwner(OwnerId));
        }

        [Fact]
        public void TryRevokeToken_WriteFailure_KeepsLiveStateUnchanged()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            manager.TryCreateToken(OwnerId, "stays-active", null, BuildGrants("alerts:read"), null, "u", out var entity, out _);

            var failing = new HSMServer.Core.Tests.Infrastructure.FailingDatabaseCore(_databaseCoreManager.DatabaseCore, _ => false)
            {
                ShouldFailApiTokenOp = op => op == "PutApiToken",
            };

            using var failingManager = new ApiTokenManager(failing, NullLogger<ApiTokenManager>.Instance);
            failingManager.Initialize().Wait();

            Assert.False(failingManager.TryRevokeToken(entity.EntityId, "u", null, out _));
            Assert.Null(failingManager.GetToken(entity.TokenId).RevokedAtUtc);
        }

        [Fact]
        public void TryRotateToken_WriteFailure_SourceTokenUnchanged()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            manager.TryCreateToken(OwnerId, "no-rotation", null, BuildGrants("alerts:read"), null, "u", out var entity, out _);

            var failing = new HSMServer.Core.Tests.Infrastructure.FailingDatabaseCore(_databaseCoreManager.DatabaseCore, _ => false)
            {
                ShouldFailApiTokenOp = op => op == "TryRotateApiToken",
            };

            using var failingManager = new ApiTokenManager(failing, NullLogger<ApiTokenManager>.Instance);
            failingManager.Initialize().Wait();

            Assert.False(failingManager.TryRotateToken(entity.EntityId, null, "u", out _, out _));
            Assert.Null(failingManager.GetToken(entity.TokenId).RevokedAtUtc);
            Assert.Equal(1, failingManager.CountQuotaEligibleTokens(OwnerId));
        }


        private static List<ApiTokenGrantEntity> BuildGrants(params string[] operations)
        {
            var grants = new List<ApiTokenGrantEntity>(operations.Length);

            foreach (var operation in operations)
                grants.Add(new ApiTokenGrantEntity
                {
                    Operation = operation,
                    BoundaryKind = (byte)ApiTokenBoundaryKind.Global,
                });

            return grants;
        }

        private static string SecretPart(string fullToken) => fullToken[(fullToken.IndexOf('.') + 1)..];

        private static string Base64UrlToBase64(string base64Url)
        {
            var padded = base64Url.Replace('-', '+').Replace('_', '/');

            return padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        }
    }


    public class ApiTokenManagerFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(ApiTokenManagerTests);
    }
}
