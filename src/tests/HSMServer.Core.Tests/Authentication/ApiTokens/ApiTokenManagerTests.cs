using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        public void Initialize_UnreadableTokenScan_FailsClosedInsteadOfReportingAnEmptyHealthyIndex()
        {
            // A failed boot scan must not present an empty index as a fresh install:
            // every existing token would silently stop authenticating while health
            // reports true. The store propagates scan failures; the manager gates
            // health on them like it does on unreadable generations.
            _databaseCoreManager.DatabaseCore.PutApiToken(new ApiTokenEntity
            {
                EntityVersion = 1,
                EntityId = Guid.NewGuid(),
                TokenId = new string('A', ApiTokenMaterial.TokenIdLength),
                VersionByte = ApiTokenMaterial.CurrentVersionByte,
                Verifier = new byte[32],
                OwnerUserId = OwnerId,
                Name = "existing-before-scan-failure",
                Grants = BuildGrants("alerts:read"),
                CreatedAtUtc = DateTime.UtcNow.Ticks,
            });

            var failing = new HSMServer.Core.Tests.Infrastructure.FailingDatabaseCore(_databaseCoreManager.DatabaseCore, _ => false)
            {
                ShouldFailApiTokenOp = op => op == "GetAllApiTokens",
            };

            using var manager = new ApiTokenManager(failing, NullLogger<ApiTokenManager>.Instance);

            manager.Initialize().Wait();

            Assert.False(manager.IsGenerationStateHealthy);
        }

        [Fact]
        public void RemoveApiToken_FailedRemoval_ReportsFalseSoRetentionSkipsUnpublish()
        {
            // The retention flow is remove-durable-first, then Unpublish. A removal
            // failure must report false so the caller never unpublishes a record whose
            // durable row may still exist (it would rejoin the index after restart).
            var failing = new HSMServer.Core.Tests.Infrastructure.FailingDatabaseCore(_databaseCoreManager.DatabaseCore, _ => false)
            {
                ShouldFailApiTokenOp = op => op == "RemoveApiToken",
            };

            Assert.False(failing.RemoveApiToken(new string('A', ApiTokenMaterial.TokenIdLength)));

            // The underlying store reports true once the row is gone, so the happy path
            // of the retention flow still unpublishes.
            var tokenId = new string('Q', ApiTokenMaterial.TokenIdLength);

            _databaseCoreManager.DatabaseCore.PutApiToken(new ApiTokenEntity
            {
                EntityVersion = 1,
                EntityId = Guid.NewGuid(),
                TokenId = tokenId,
                VersionByte = ApiTokenMaterial.CurrentVersionByte,
                Verifier = new byte[32],
                OwnerUserId = OwnerId,
                Name = "removable",
                Grants = BuildGrants("alerts:read"),
                CreatedAtUtc = DateTime.UtcNow.Ticks,
            });

            Assert.True(_databaseCoreManager.DatabaseCore.RemoveApiToken(tokenId));
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
            Assert.True(ApiTokenMaterial.TryParse(fullToken, out var tokenIdBytes, out _));

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
        public void TryRestrictToken_NullGrants_KeepCurrentGrants()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            manager.TryCreateToken(OwnerId, "keep-grants", null, BuildGrants("alerts:read", "alerts:write"),
                DateTime.UtcNow.AddYears(1), "u", out var entity, out _);

            var shorterExpiry = DateTime.UtcNow.AddDays(1);

            // Null means "not changing the grants" — symmetric with null expiry — and must
            // never strip the token's authorization while shortening the expiry.
            Assert.True(manager.TryRestrictToken(entity.EntityId, null, shorterExpiry, "u", out var restricted));

            Assert.Equal(2, restricted.Grants.Count);
            Assert.Equal(shorterExpiry.ToUniversalTime().Ticks, restricted.ExpiresAtUtc.Value);
        }

        [Fact]
        public void TryRestrictToken_AfterEmergencyRevoke_IsRejected()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            manager.TryCreateToken(OwnerId, "generation-dead", null, BuildGrants("alerts:read"), null, "u", out var entity, out _);

            // Emergency revoke advances the generation; the record keeps RevokedAtUtc == null.
            manager.AdvanceOwnerRevocationGeneration(OwnerId);

            Assert.False(manager.TryRestrictToken(entity.EntityId, BuildGrants("alerts:read"), null, "u", out _));
            Assert.Null(manager.GetToken(entity.TokenId).RestrictedAtUtc);
        }

        [Fact]
        public void TryRestrictToken_NoOpRequest_SucceedsWithoutRewrite()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            manager.TryCreateToken(OwnerId, "no-op", null, BuildGrants("alerts:read"), DateTime.UtcNow.AddYears(1), "u", out var entity, out _);

            // Same grants (null = keep) and unchanged expiry (null = keep): true, but no
            // audit stamp and no durable write — nothing changed.
            Assert.True(manager.TryRestrictToken(entity.EntityId, null, null, "u", out var unchanged));

            Assert.Null(unchanged.RestrictedAtUtc);

            using var reopened = CreateManager();
            reopened.Initialize().Wait();

            Assert.Null(reopened.GetToken(entity.TokenId).RestrictedAtUtc);
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
        public void TryRotateToken_AfterEmergencyRevoke_IsRefused()
        {
            // An emergency revoke kills records by advancing a generation, leaving
            // RevokedAtUtc null. Rotating such a record must not mint a live replacement
            // stamped with the current generation — that would silently undo the revoke.
            using var manager = CreateManager();
            manager.Initialize().Wait();

            manager.TryCreateToken(OwnerId, "global-killed", null, BuildGrants("alerts:read"), null, "u", out var globalKilled, out _);

            manager.AdvanceGlobalRevocationGeneration();

            Assert.False(manager.TryRotateToken(globalKilled.EntityId, null, "u", out _, out _));
            Assert.Null(manager.GetToken(globalKilled.TokenId).RevokedAtUtc);
            Assert.Single(manager.GetTokensByOwner(OwnerId));
            Assert.Equal(0, manager.CountQuotaEligibleTokens(OwnerId));

            // The owner-scoped emergency revoke is refused the same way.
            manager.TryCreateToken(OwnerId, "owner-killed", null, BuildGrants("alerts:read"), null, "u", out var ownerKilled, out _);

            manager.AdvanceOwnerRevocationGeneration(OwnerId);

            Assert.False(manager.TryRotateToken(ownerKilled.EntityId, null, "u", out _, out _));
            Assert.Null(manager.GetToken(ownerKilled.TokenId).RevokedAtUtc);
            Assert.Equal(2, manager.GetTokensByOwner(OwnerId).Count);
            Assert.Equal(0, manager.CountQuotaEligibleTokens(OwnerId));

            // Durable as well: a fresh index sees no replacement rows.
            using var reopened = CreateManager();
            reopened.Initialize().Wait();

            Assert.Equal(2, reopened.GetTokensByOwner(OwnerId).Count);
            Assert.All(reopened.GetTokensByOwner(OwnerId), token => Assert.Null(token.RotatedAtUtc));
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


        [Fact]
        public void TryCreateToken_UnspecifiedKindExpiry_IsInterpretedAsUtc()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            // An offset-less form/JSON value has Kind.Unspecified: it must be read as the
            // UTC time it names, never converted from the server's local zone.
            var expiry = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(7).Date.AddHours(12), DateTimeKind.Unspecified);

            Assert.True(manager.TryCreateToken(OwnerId, "utc-by-contract", null, BuildGrants("alerts:read"), expiry, "u", out var entity, out _));

            Assert.Equal(expiry.Ticks, entity.ExpiresAtUtc.Value);
        }

        [Fact]
        public void TryCreateToken_SanitizesAndBoundsFreeText()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            var longName = new string('n', 512);
            var longDescription = $"first line{Environment.NewLine}second\x0000line {new string('d', 2048)}";

            Assert.True(manager.TryCreateToken(OwnerId, longName, longDescription, BuildGrants("alerts:read"), null, "u", out var entity, out _));

            Assert.Equal(256, entity.Name.Length);
            Assert.Equal(new string('n', 256), entity.Name);

            // Control characters are neutralized and the description is bounded.
            Assert.True(entity.Description.Length <= 1024);
            Assert.All(entity.Description, c => Assert.False(char.IsControl(c)));

            // Actor fields get the same treatment as free text.
            Assert.True(manager.TryCreateToken(OwnerId, "actor-sanitize", null, BuildGrants("alerts:read"), null,
                "attacker\r\nadmin", out var actorEntity, out _));

            Assert.Equal("attacker  admin", actorEntity.CreatedBy);

            // The revocation reason and the revoking actor are sanitized the same way:
            // each control character becomes one space, so nothing can forge log lines.
            manager.TryRevokeToken(entity.EntityId, "attacker\x0000", "forged\r\nsecond line", out var revoked);

            Assert.Equal("attacker", revoked.RevokedBy);
            Assert.Equal("forged  second line", revoked.RevocationReason);
        }

        [Fact]
        public void TryCreateToken_TruncationNeverSplitsASurrogatePair()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            // 255 chars + a 2-char surrogate pair: cutting at 256 would leave a lone high
            // surrogate in the live entity while the JSON row holds U+FFFD after a
            // round-trip — the cut must back off to 255 instead.
            var surrogateName = $"{new string('n', 255)}\U0001F600";

            Assert.True(manager.TryCreateToken(OwnerId, surrogateName, null, BuildGrants("alerts:read"), null, "u", out var entity, out _));

            Assert.Equal(255, entity.Name.Length);
            Assert.All(entity.Name, c => Assert.False(char.IsSurrogate(c)));
        }

        [Fact]
        public void TryCreateToken_UnpairedSurrogate_IsReplacedLikeTheJsonRoundTripWould()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            // A lone surrogate would become U+FFFD only in the durable row; replacing it
            // during sanitization keeps the live entity and the row identical.
            manager.TryCreateToken(OwnerId, "lone\uD800high", "low\uDC00half", BuildGrants("alerts:read"), null, "u", out var entity, out _);

            Assert.Equal("lone�high", entity.Name);
            Assert.Equal("low�half", entity.Description);

            using var reopened = CreateManager();
            reopened.Initialize().Wait();

            Assert.Equal(entity.Name, reopened.GetToken(entity.TokenId).Name);
            Assert.Equal(entity.Description, reopened.GetToken(entity.TokenId).Description);
        }

        [Fact]
        public void TryCreateToken_TruncatedNameNeverEndsInAReplacedControlCharacterSpace()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            // 255 'n', a NUL (becomes a space at index 255), then a tail: the 256-char cut
            // lands right after the replaced space, and the result must re-trim it.
            var name = $"{new string('n', 255)}\0tail";

            Assert.True(manager.TryCreateToken(OwnerId, name, null, BuildGrants("alerts:read"), null, "u", out var entity, out _));

            Assert.Equal(255, entity.Name.Length);
            Assert.Equal(new string('n', 255), entity.Name);
        }

        [Fact]
        public void Initialize_NonCanonicalBoundaryIdRow_LoadsCanonicalGrantsAndRestricts()
        {
            var productId = Guid.NewGuid();
            var tokenId = new string('A', ApiTokenMaterial.TokenIdLength);

            _databaseCoreManager.DatabaseCore.PutApiToken(new ApiTokenEntity
            {
                EntityVersion = 1,
                EntityId = Guid.NewGuid(),
                TokenId = tokenId,
                VersionByte = ApiTokenMaterial.CurrentVersionByte,
                Verifier = new byte[32],
                OwnerUserId = OwnerId,
                Name = "non-canonical-row",
                Grants =
                [
                    new ApiTokenGrantEntity
                    {
                        Operation = ApiTokenOperations.ProductsRead,
                        BoundaryKind = (byte)ApiTokenBoundaryKind.Product,
                        BoundaryId = productId.ToString("B"), // parses, but not the "D" form
                    },
                ],
                CreatedAtUtc = DateTime.UtcNow.Ticks,
            });

            using var manager = CreateManager();
            manager.Initialize().Wait();

            var loaded = manager.GetToken(tokenId);

            Assert.NotNull(loaded);
            Assert.Equal(productId.ToString(), loaded.Grants[0].BoundaryId);

            // Without load-time canonicalization this removal is rejected: the canonical
            // "D"-form pair never matches the stored non-canonical grant.
            Assert.True(manager.TryRestrictToken(loaded.EntityId, [], null, "u", out var restricted));
            Assert.Empty(restricted.Grants);
        }

        [Fact]
        public void Initialize_NullGrantsRow_IsSkipped()
        {
            var entityId = Guid.NewGuid();

            _databaseCoreManager.DatabaseCore.PutApiToken(new ApiTokenEntity
            {
                EntityVersion = 1,
                EntityId = entityId,
                // Canonical shape ('A' has zero trailing bits): the TokenId check must
                // pass so the row is rejected by the null grants alone.
                TokenId = new string('A', ApiTokenMaterial.TokenIdLength),
                VersionByte = ApiTokenMaterial.CurrentVersionByte,
                Verifier = new byte[32],
                OwnerUserId = OwnerId,
                Name = "null-grants-row",
                Grants = null,
                CreatedAtUtc = DateTime.UtcNow.Ticks,
            });

            using var manager = CreateManager();
            manager.Initialize().Wait();

            Assert.Null(manager.GetTokenByEntityId(entityId));
        }

        [Fact]
        public void TryRestrictToken_RevokedToken_IsRejected()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            manager.TryCreateToken(OwnerId, "dead", null, BuildGrants("alerts:read"), null, "u", out var entity, out _);
            manager.TryRevokeToken(entity.EntityId, "u", "gone", out _);

            Assert.False(manager.TryRestrictToken(entity.EntityId, BuildGrants("alerts:read"), null, "u", out _));
        }

        [Fact]
        public void TryRotateToken_PastRequestedOrInheritedExpiry_IsRefused()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            // Requested shortening into the past: the create-time rule, mirrored.
            manager.TryCreateToken(OwnerId, "requested-past", null, BuildGrants("alerts:read"), null, "u", out var entity, out _);

            Assert.False(manager.TryRotateToken(entity.EntityId, DateTime.UtcNow.AddDays(-1), "u", out _, out _));
            Assert.Null(manager.GetToken(entity.TokenId).RevokedAtUtc);

            // An already-expired source: the replacement would inherit a dead expiry, and
            // its one-time secret would be disclosed for nothing.
            var expiredEntityId = Guid.NewGuid();

            _databaseCoreManager.DatabaseCore.PutApiToken(new ApiTokenEntity
            {
                EntityVersion = 1,
                EntityId = expiredEntityId,
                TokenId = new string('Q', ApiTokenMaterial.TokenIdLength),
                VersionByte = ApiTokenMaterial.CurrentVersionByte,
                Verifier = new byte[32],
                OwnerUserId = OwnerId,
                Name = "already-expired",
                Grants = BuildGrants("alerts:read"),
                CreatedAtUtc = DateTime.UtcNow.AddDays(-10).Ticks,
                ExpiresAtUtc = DateTime.UtcNow.AddDays(-1).Ticks,
            });

            using var reopened = CreateManager();
            reopened.Initialize().Wait();

            Assert.False(reopened.TryRotateToken(expiredEntityId, null, "u", out _, out _));
        }

        [Fact]
        public void Unpublish_RemovesTheRecordFromTheLiveIndex()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            manager.TryCreateToken(OwnerId, "to-unpublish", null, BuildGrants("alerts:read"), null, "u", out var entity, out _);

            Assert.True(manager.Unpublish(entity.TokenId));

            Assert.Null(manager.GetToken(entity.TokenId));
            Assert.Null(manager.GetTokenByEntityId(entity.EntityId));
            Assert.Empty(manager.GetTokensByOwner(OwnerId));
            Assert.Equal(0, manager.CountQuotaEligibleTokens(OwnerId));

            // Idempotent on the live index; the durable row is the caller's concern.
            Assert.False(manager.Unpublish(entity.TokenId));
            Assert.False(manager.Unpublish(null));
        }

        [Fact]
        public void TryCreateToken_UnhealthyGenerationState_IsRefusedWithNoDurableState()
        {
            // Boot fails to prove the generation state authoritative: minting must be
            // refused outright, or the credential would be silently generation-invalidated
            // after the operator repairs the rows and restarts.
            var failing = new HSMServer.Core.Tests.Infrastructure.FailingDatabaseCore(_databaseCoreManager.DatabaseCore, _ => false)
            {
                ShouldFailApiTokenOp = op => op == "GetGlobalRevocationGeneration",
            };

            using var manager = new ApiTokenManager(failing, NullLogger<ApiTokenManager>.Instance);
            manager.Initialize().Wait();

            Assert.False(manager.IsGenerationStateHealthy);

            Assert.False(manager.TryCreateToken(OwnerId, "doomed", null, BuildGrants("alerts:read"), null, "u", out _, out _));
            Assert.Empty(manager.GetTokensByOwner(OwnerId));

            // Nothing reached the durable store either: a fresh index sees no tokens.
            using var reopened = CreateManager();
            reopened.Initialize().Wait();

            Assert.Empty(reopened.GetTokensByOwner(OwnerId));
        }

        [Fact]
        public void TryRotateToken_UnhealthyGenerationState_IsRefused()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            manager.TryCreateToken(OwnerId, "no-rotate-unhealthy", null, BuildGrants("alerts:read"), null, "u", out var entity, out _);

            var failing = new HSMServer.Core.Tests.Infrastructure.FailingDatabaseCore(_databaseCoreManager.DatabaseCore, _ => false)
            {
                ShouldFailApiTokenOp = op => op == "GetGlobalRevocationGeneration",
            };

            using var failingManager = new ApiTokenManager(failing, NullLogger<ApiTokenManager>.Instance);
            failingManager.Initialize().Wait();

            Assert.False(failingManager.IsGenerationStateHealthy);
            Assert.False(failingManager.TryRotateToken(entity.EntityId, null, "u", out _, out _));
            Assert.Null(failingManager.GetToken(entity.TokenId).RevokedAtUtc);
        }

        [Fact]
        public void TryCreateToken_UnreadableOwnerGeneration_ReturnsFalseInsteadOfThrowing()
        {
            // A corrupt ApiTokenGeneration_Owner_ row for an owner absent from the cache is
            // only discovered by the create-time fallback read: it must surface as false,
            // not as an exception escaping a Try* method.
            var failing = new HSMServer.Core.Tests.Infrastructure.FailingDatabaseCore(_databaseCoreManager.DatabaseCore, _ => false)
            {
                ShouldFailApiTokenOp = op => op == "GetOwnerRevocationGeneration",
            };

            using var manager = new ApiTokenManager(failing, NullLogger<ApiTokenManager>.Instance);
            manager.Initialize().Wait();

            // The owner has no records, so the load path read nothing and proved nothing.
            Assert.True(manager.IsGenerationStateHealthy);

            Assert.False(manager.TryCreateToken(Guid.NewGuid(), "corrupt-owner-generation", null, BuildGrants("alerts:read"), null, "u", out _, out _));
        }

        [Fact]
        public void TryCreateToken_OwnerAbsentFromGenerationCache_UsesDurableOwnerGeneration()
        {
            // Retention can remove every record of an owner whose generation was advanced:
            // the durable counter then outlives the in-memory index, and a new token must
            // be stamped with it, not with the missing-as-zero default.
            var orphanOwner = Guid.NewGuid();

            _databaseCoreManager.DatabaseCore.AdvanceOwnerRevocationGeneration(orphanOwner);
            _databaseCoreManager.DatabaseCore.AdvanceOwnerRevocationGeneration(orphanOwner);
            Assert.Equal(3, _databaseCoreManager.DatabaseCore.AdvanceOwnerRevocationGeneration(orphanOwner));

            using var manager = CreateManager();
            manager.Initialize().Wait();

            // No loadable records for this owner, so the load path never cached a value.
            Assert.Equal(0, manager.GetOwnerRevocationGeneration(orphanOwner));

            Assert.True(manager.TryCreateToken(orphanOwner, "post-cleanup", null, BuildGrants("alerts:read"), null, "u", out var entity, out _));
            Assert.Equal(3, entity.OwnerRevocationGenerationAtIssue);

            // Consistent in-process: the fallback is cached, so the token counts.
            Assert.Equal(1, manager.CountQuotaEligibleTokens(orphanOwner));

            // And consistent across restart: the durable generation still matches the stamp.
            using var reopened = CreateManager();
            reopened.Initialize().Wait();

            Assert.True(reopened.IsGenerationStateHealthy);
            Assert.Equal(1, reopened.CountQuotaEligibleTokens(orphanOwner));
        }

        [Fact]
        public void ConcurrentCreateAndEnumerate_OneOwner_AllTokensPublishedSafely()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            const int tokens = 32;

            var creating = Enumerable.Range(0, tokens)
                .Select(i => Task.Run(() => manager.TryCreateToken(OwnerId, $"parallel-{i}", null, BuildGrants("alerts:read"), null, "u", out _, out _)))
                .ToArray();

            // Enumeration runs against the same owner index the creates publish into.
            var enumerating = Task.Run(() =>
            {
                for (var i = 0; i < 10_000; i++)
                    _ = manager.GetTokensByOwner(OwnerId).Count;
            });

            Task.WaitAll([.. creating, enumerating]);

            Assert.All(creating, task => Assert.True(task.Result));
            Assert.Equal(tokens, manager.GetTokensByOwner(OwnerId).Count);
            Assert.Equal(tokens, manager.CountQuotaEligibleTokens(OwnerId));
        }

        [Fact]
        public void ConcurrentRevokeVersusRestrict_RevocationIsNeverLost()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            var tokenIds = new List<string>();

            for (var round = 0; round < 20; round++)
            {
                manager.TryCreateToken(OwnerId, $"race-restrict-{round}", null, BuildGrants("alerts:read", "alerts:write"), null, "u", out var entity, out _);
                tokenIds.Add(entity.TokenId);

                using var start = new Barrier(2);

                var revoking = Task.Run(() =>
                {
                    start.SignalAndWait();
                    return manager.TryRevokeToken(entity.EntityId, "u", "race", out _);
                });
                var restricting = Task.Run(() =>
                {
                    start.SignalAndWait();
                    return manager.TryRestrictToken(entity.EntityId, BuildGrants("alerts:read"), null, "u", out _);
                });

                Task.WaitAll(revoking, restricting);

                // Whoever wins, the revocation must survive the concurrent restrict.
                Assert.True(revoking.Result);
                Assert.NotNull(manager.GetToken(entity.TokenId).RevokedAtUtc);
            }

            // Durable as well: a fresh index sees every raced token revoked.
            using var reopened = CreateManager();
            reopened.Initialize().Wait();

            foreach (var tokenId in tokenIds)
                Assert.NotNull(reopened.GetToken(tokenId).RevokedAtUtc);
        }

        [Fact]
        public void ConcurrentRevokeVersusRotate_SourceTokenAlwaysRevoked()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            var tokenIds = new List<string>();

            for (var round = 0; round < 20; round++)
            {
                manager.TryCreateToken(OwnerId, $"race-rotate-{round}", null, BuildGrants("alerts:read"), null, "u", out var entity, out _);
                tokenIds.Add(entity.TokenId);

                using var start = new Barrier(2);

                var revoking = Task.Run(() =>
                {
                    start.SignalAndWait();
                    return manager.TryRevokeToken(entity.EntityId, "u", "race", out _);
                });
                var rotating = Task.Run(() =>
                {
                    start.SignalAndWait();
                    return manager.TryRotateToken(entity.EntityId, null, "u", out _, out _);
                });

                Task.WaitAll(revoking, rotating);

                Assert.True(revoking.Result);
                Assert.NotNull(manager.GetToken(entity.TokenId).RevokedAtUtc);
            }

            using var reopened = CreateManager();
            reopened.Initialize().Wait();

            foreach (var tokenId in tokenIds)
                Assert.NotNull(reopened.GetToken(tokenId).RevokedAtUtc);
        }

        [Fact]
        public void ConcurrentAdvanceGenerations_InMemoryMatchesDurableAndNeverRegresses()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            const int advances = 32;
            var owner = Guid.NewGuid();

            var globalResults = Enumerable.Range(0, advances)
                .Select(_ => Task.Run(manager.AdvanceGlobalRevocationGeneration))
                .ToArray();
            var ownerResults = Enumerable.Range(0, advances)
                .Select(_ => Task.Run(() => manager.AdvanceOwnerRevocationGeneration(owner)))
                .ToArray();

            var allGlobal = Task.WhenAll(globalResults).Result;
            var allOwner = Task.WhenAll(ownerResults).Result;

            // Every advance is durable exactly once and returned exactly once.
            Assert.Equal(Enumerable.Range(1, advances).Select(i => (long)i), allGlobal.OrderBy(_ => _));
            Assert.Equal(Enumerable.Range(1, advances).Select(i => (long)i), allOwner.OrderBy(_ => _));

            // The in-memory values equal the durable counters, not a stale one.
            Assert.Equal(_databaseCoreManager.DatabaseCore.GetGlobalRevocationGeneration(), manager.GlobalRevocationGeneration);
            Assert.Equal(_databaseCoreManager.DatabaseCore.GetOwnerRevocationGeneration(owner), manager.GetOwnerRevocationGeneration(owner));
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
