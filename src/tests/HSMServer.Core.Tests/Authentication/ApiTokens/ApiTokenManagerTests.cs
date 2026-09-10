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
    // Lifecycle contract of the authoritative token index (#1384 owner-mirrored model):
    // persist-first publication, one-time secret disclosure, rename-only mutation,
    // flag-preserving rotation, idempotent revocation, durable revocation generations,
    // quota counting semantics, and the fail-closed load of pre-simplification rows.
    [Collection("Database collection")]
    public class ApiTokenManagerTests : DatabaseCoreTestsBase<ApiTokenManagerFixture>, IClassFixture<DatabaseRegisterFixture>
    {
        private static readonly Guid OwnerId = Guid.NewGuid();


        public ApiTokenManagerTests(ApiTokenManagerFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture) { }


        // 0 = unlimited through the internal test ctor; the production ctor takes the
        // validated config (MaxTokensPerUser >= 1) and throws on null.
        private ApiTokenManager CreateManager() =>
            new(_databaseCoreManager.DatabaseCore, NullLogger<ApiTokenManager>.Instance);


        [Fact]
        public void TryCreateToken_AtConfiguredQuota_RefusesSecondLiveToken()
        {
            // The hard quota bound lives INSIDE the manager's state-locked create path
            // (its ApiTokens.MaxTokensPerUser), not in a caller-side pre-check: two
            // concurrent creates cannot both pass such a check and exceed the cap.
            using var capped = new ApiTokenManager(_databaseCoreManager.DatabaseCore,
                NullLogger<ApiTokenManager>.Instance,
                new HSMServer.ServerConfiguration.ApiTokensConfig { MaxTokensPerUser = 1 });

            capped.Initialize().Wait();

            Assert.True(capped.TryCreateToken(OwnerId, "first", readOnly: false, "u", out _, out _));
            Assert.False(capped.TryCreateToken(OwnerId, "second", readOnly: false, "u", out _, out _));
            Assert.Equal(1, capped.CountQuotaEligibleTokens(OwnerId));

            // A revoked record no longer counts: the slot frees without reconciliation.
            var first = capped.GetTokensByOwner(OwnerId)[0];
            Assert.True(capped.TryRevokeToken(first.EntityId, "u", "cap test", out _));
            Assert.True(capped.TryCreateToken(OwnerId, "third", readOnly: false, "u", out _, out _));
        }


        [Fact]
        public void CountQuotaEligibleTokensGlobally_CountsOnlyLiveTokensAcrossOwners()
        {
            // The advisory count an emergency revoke-all reports: the same IsLive rule
            // as the per-owner quota counter, judged across every owner at once.
            using var manager = CreateManager();
            manager.Initialize().Wait();

            var otherOwner = Guid.NewGuid();

            Assert.True(manager.TryCreateToken(OwnerId, "live-a", readOnly: false, "u", out _, out _));
            Assert.True(manager.TryCreateToken(otherOwner, "live-b", readOnly: false, "u", out _, out _));
            Assert.True(manager.TryCreateToken(OwnerId, "revoked", readOnly: false, "u", out var revoked, out _));
            Assert.True(manager.TryRevokeToken(revoked.EntityId, "u", "count test", out _));

            Assert.Equal(2, manager.CountQuotaEligibleTokensGlobally());

            // An owner-scoped emergency revoke removes that owner's live token from the
            // global count without touching the other owner's.
            manager.AdvanceOwnerRevocationGeneration(OwnerId);

            Assert.Equal(1, manager.CountQuotaEligibleTokensGlobally());

            // A token minted after the advance is stamped at the new generation and
            // counts again.
            Assert.True(manager.TryCreateToken(OwnerId, "fresh", readOnly: false, "u", out _, out _));

            Assert.Equal(2, manager.CountQuotaEligibleTokensGlobally());

            // The deployment-wide lever empties the count; a fresh token after it counts.
            manager.AdvanceGlobalRevocationGeneration();

            Assert.Equal(0, manager.CountQuotaEligibleTokensGlobally());

            Assert.True(manager.TryCreateToken(otherOwner, "fresh-b", readOnly: false, "u", out _, out _));

            Assert.Equal(1, manager.CountQuotaEligibleTokensGlobally());
        }


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
            _databaseCoreManager.DatabaseCore.PutApiToken(BuildRow(name: "existing-before-scan-failure"));

            var failing = new HSMServer.Core.Tests.Infrastructure.FailingDatabaseCore(_databaseCoreManager.DatabaseCore, _ => false)
            {
                ShouldFailApiTokenOp = op => op == "GetAllApiTokens",
            };

            using var manager = new ApiTokenManager(failing, NullLogger<ApiTokenManager>.Instance);

            manager.Initialize().Wait();

            Assert.False(manager.IsGenerationStateHealthy);
        }


        [Fact]
        public void TryRevokeToken_AfterFailedBootScan_ReportsFalse_EmergencyRevokeIsTheLever()
        {
            // The index is empty after a failed scan but the durable rows survive: the
            // per-token revoke cannot reach them and reports false. The operator lever in
            // that state is the emergency revoke, which bypasses the index and acts
            // durably — this pins that contract so the management layer can key its
            // "unhealthy, use emergency revoke" messaging on the health flag.
            var entityId = Guid.NewGuid();
            var tokenId = new string('A', ApiTokenMaterial.TokenIdLength);

            _databaseCoreManager.DatabaseCore.PutApiToken(BuildRow(entityId: entityId, tokenId: tokenId,
                name: "compromised-but-unreachable"));

            var failing = new HSMServer.Core.Tests.Infrastructure.FailingDatabaseCore(_databaseCoreManager.DatabaseCore, _ => false)
            {
                ShouldFailApiTokenOp = op => op == "GetAllApiTokens",
            };

            using var manager = new ApiTokenManager(failing, NullLogger<ApiTokenManager>.Instance);

            manager.Initialize().Wait();

            Assert.False(manager.IsGenerationStateHealthy);
            Assert.False(manager.TryRevokeToken(entityId, "u", "compromised", out _));

            // The durable row is untouched by the per-token attempt...
            Assert.NotNull(_databaseCoreManager.DatabaseCore.GetApiToken(tokenId));

            // ...but the emergency revoke still kills it durably, bypassing the index.
            manager.AdvanceGlobalRevocationGeneration();

            using var repaired = CreateManager();
            repaired.Initialize().Wait();

            // Generation-invalidated: can never authenticate even though RevokedAtUtc is null.
            Assert.Equal(1, repaired.GlobalRevocationGeneration);
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

            _databaseCoreManager.DatabaseCore.PutApiToken(BuildRow(tokenId: tokenId, name: "removable"));

            Assert.True(_databaseCoreManager.DatabaseCore.RemoveApiToken(tokenId));
        }


        [Fact]
        public void TryAuthenticate_ValidToken_ReturnsTheLiveRecord()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            manager.TryCreateToken(OwnerId, "auth-me", readOnly: false, "creator", out var entity, out var fullToken);

            Assert.True(manager.TryAuthenticate(fullToken, out var authenticated));

            Assert.Equal(entity.EntityId, authenticated.EntityId);
        }


        [Fact]
        public void TryAuthenticate_EveryFailClosedReason_ReturnsFalse()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            // Garbage never reaches the database.
            Assert.False(manager.TryAuthenticate(null, out _));
            Assert.False(manager.TryAuthenticate("hsm_pat_v1_garbage", out _));

            // Unknown but well-formed id: same shape as a real token, unknown TokenId.
            var unknown = $"hsm_pat_v1_{new string('A', ApiTokenMaterial.TokenIdLength)}.{new string('A', ApiTokenMaterial.SecretLength)}";
            Assert.False(manager.TryAuthenticate(unknown, out _));

            manager.TryCreateToken(OwnerId, "auth-checks", readOnly: false, "u", out var entity, out var fullToken);

            // Wrong secret: same canonical shape ('E' has zero trailing bits), different bits.
            var tampered = fullToken[..^1] + (fullToken[^1] == 'A' ? 'E' : 'A');
            Assert.NotEqual(fullToken, tampered);
            Assert.False(manager.TryAuthenticate(tampered, out _));

            // Revoked.
            manager.TryRevokeToken(entity.EntityId, "u", "gone", out _);
            Assert.False(manager.TryAuthenticate(fullToken, out _));

            // Generation-invalidated (global and owner emergency revoke).
            manager.TryCreateToken(OwnerId, "global-killed-auth", readOnly: false, "u", out var globalKilled, out var globalToken);
            manager.AdvanceGlobalRevocationGeneration();
            Assert.False(manager.TryAuthenticate(globalToken, out _));

            manager.TryCreateToken(OwnerId, "owner-killed-auth", readOnly: false, "u", out var ownerKilled, out var ownerToken);
            manager.AdvanceOwnerRevocationGeneration(OwnerId);
            Assert.False(manager.TryAuthenticate(ownerToken, out _));
        }


        [Fact]
        public void TryAuthenticate_PreSimplificationRowWithGrants_FailsClosedAtLoad()
        {
            // #1384: a fine-granted record's power profile no longer exists. Loading it
            // as a full owner mirror would WIDEN a deliberately narrow credential, so
            // the row is rejected at load and its bearer cannot authenticate at all —
            // the holder re-mints under the simplified model.
            var tokenId = new string('Q', ApiTokenMaterial.TokenIdLength);

            _databaseCoreManager.DatabaseCore.PutApiToken(BuildRow(tokenId: tokenId, name: "pre-simplification") with
            {
                Grants = [new ApiTokenGrantEntity { Operation = "alerts:read", BoundaryKind = (byte)ApiTokenBoundaryKind.Global }],
            });

            using var manager = CreateManager();
            manager.Initialize().Wait();

            Assert.Null(manager.GetToken(tokenId));
            Assert.Equal(new[] { tokenId }, manager.GetOrphanTokenIds());
        }


        [Fact]
        public void TryAuthenticate_PreSimplificationRowWithExpiry_FailsClosedAtLoad()
        {
            // Same rule for the expiry-carrying shape: the row is a pre-simplification
            // record, not an eternal mirror, and must not load.
            var tokenId = new string('R', ApiTokenMaterial.TokenIdLength);

            _databaseCoreManager.DatabaseCore.PutApiToken(BuildRow(tokenId: tokenId, name: "expired-world") with
            {
                ExpiresAtUtc = DateTime.UtcNow.AddDays(30).Ticks,
            });

            using var manager = CreateManager();
            manager.Initialize().Wait();

            Assert.Null(manager.GetToken(tokenId));
            Assert.Equal(new[] { tokenId }, manager.GetOrphanTokenIds());
        }


        [Fact]
        public void TryAuthenticate_PreSimplificationRowWithRestrictionStamps_FailsClosedAtLoad()
        {
            // Unreachable through the old surface (a restricted record also carried
            // grants), but the invariant is "pre-simplification rows fail closed": a
            // restriction stamp alone marks the row as pre-simplification.
            var tokenId = new string('S', ApiTokenMaterial.TokenIdLength);

            _databaseCoreManager.DatabaseCore.PutApiToken(BuildRow(tokenId: tokenId, name: "was-restricted") with
            {
                RestrictedAtUtc = DateTime.UtcNow.AddDays(-1).Ticks,
            });

            using var manager = CreateManager();
            manager.Initialize().Wait();

            Assert.Null(manager.GetToken(tokenId));
            Assert.Equal(new[] { tokenId }, manager.GetOrphanTokenIds());
        }


        [Fact]
        public void TryAuthenticate_UnhealthyState_RefusesEvenValidCredentials()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            manager.TryCreateToken(OwnerId, "valid-but-unhealthy", readOnly: false, "u", out _, out var validToken);

            var failing = new HSMServer.Core.Tests.Infrastructure.FailingDatabaseCore(_databaseCoreManager.DatabaseCore, _ => false)
            {
                ShouldFailApiTokenOp = op => op == "GetGlobalRevocationGeneration",
            };

            using var unhealthy = new ApiTokenManager(failing, NullLogger<ApiTokenManager>.Instance);
            unhealthy.Initialize().Wait();

            Assert.False(unhealthy.IsGenerationStateHealthy);
            Assert.False(unhealthy.TryAuthenticate(validToken, out _));
        }


        [Fact]
        public void TryCreateToken_PersistsFirst_SecretDisclosedOnce()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            var created = manager.TryCreateToken(
                OwnerId, "monitoring", readOnly: true,
                createdBy: "test-user", out var entity, out var fullToken);

            Assert.True(created);
            Assert.NotNull(entity);
            Assert.True(entity.ReadOnly);
            Assert.StartsWith("hsm_pat_v1_", fullToken);
            Assert.True(ApiTokenMaterial.TryParse(fullToken, out var tokenIdBytes, out _));

            // The stored verifier matches the presented secret, but no stored field equals
            // the secret itself. Read from the store: the manager's public results are
            // verifier-free projections.
            var expectedVerifier = ApiTokenVerifier.ComputeVerifier(
                ApiTokenMaterial.CurrentVersionByte, tokenIdBytes,
                Convert.FromBase64String(Base64UrlToBase64(SecretPart(fullToken))));

            var stored = _databaseCoreManager.DatabaseCore.GetApiToken(ApiTokenMaterial.TokenIdOf(fullToken));

            Assert.Equal(expectedVerifier, stored.Verifier);
            // The simplified model's own durable shape: no grants, no expiry, the flag.
            Assert.Empty(stored.Grants);
            Assert.Null(stored.ExpiresAtUtc);
            Assert.True(stored.ReadOnly);
            Assert.Equal(entity.EntityId, manager.GetToken(ApiTokenMaterial.TokenIdOf(fullToken)).EntityId);
            Assert.Equal(entity.EntityId, manager.GetTokenByEntityId(entity.EntityId).EntityId);
            Assert.Single(manager.GetTokensByOwner(OwnerId), token => token.EntityId == entity.EntityId);
        }


        [Fact]
        public void TryCreateToken_SurvivesManagerRestart_WithTheFlag()
        {
            ApiTokenInfo entity;
            string fullToken;

            using (var manager = CreateManager())
            {
                manager.Initialize().Wait();

                manager.TryCreateToken(OwnerId, "restart-proof", readOnly: true, "test-user", out entity, out fullToken);
            }

            using var reopened = CreateManager();
            reopened.Initialize().Wait();

            var reloaded = reopened.GetToken(ApiTokenMaterial.TokenIdOf(fullToken));

            Assert.NotNull(reloaded);
            Assert.Equal(entity.EntityId, reloaded.EntityId);
            Assert.True(reloaded.ReadOnly);

            // The persisted verifier survived the restart untouched.
            Assert.Equal(32, _databaseCoreManager.DatabaseCore.GetApiToken(ApiTokenMaterial.TokenIdOf(fullToken)).Verifier.Length);
        }


        [Fact]
        public void TryCreateToken_RejectsBadInput()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            Assert.False(manager.TryCreateToken(Guid.Empty, "no owner", readOnly: false, "u", out _, out _));
            Assert.False(manager.TryCreateToken(OwnerId, "  ", readOnly: false, "u", out _, out _));
            // Over-length name is rejected, not silently shortened.
            Assert.False(manager.TryCreateToken(OwnerId, new string('n', 512), readOnly: false, "u", out _, out _));
        }


        [Fact]
        public void TryCreateToken_ManyTokens_AllUniqueAndQuotaCounted()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            var tokenIds = new HashSet<string>();

            for (var i = 0; i < 50; i++)
            {
                Assert.True(manager.TryCreateToken(OwnerId, $"token-{i}", readOnly: false, "u", out _, out var fullToken));
                tokenIds.Add(ApiTokenMaterial.TokenIdOf(fullToken));
            }

            Assert.Equal(50, tokenIds.Count);
            Assert.Equal(50, manager.CountQuotaEligibleTokens(OwnerId));
        }


        [Fact]
        public void TryRevokeToken_IsImmediateAndIdempotent()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            manager.TryCreateToken(OwnerId, "to-revoke", readOnly: false, "u", out var entity, out var fullToken);

            Assert.True(manager.TryRevokeToken(entity.EntityId, "test-user", "rotation cleanup", out var revoked));
            Assert.NotNull(revoked.RevokedAtUtc);

            var firstRevokedAt = revoked.RevokedAtUtc;

            Assert.True(manager.TryRevokeToken(entity.EntityId, "test-user", "again", out var again));
            Assert.Equal(firstRevokedAt, again.RevokedAtUtc);

            // Revoked tokens never count toward the quota.
            Assert.Equal(0, manager.CountQuotaEligibleTokens(OwnerId));
        }


        [Fact]
        public void TryRenameToken_PersistsTheNewNameAcrossRestart()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            manager.TryCreateToken(OwnerId, "before-rename", readOnly: true, "u", out var entity, out var fullToken);

            Assert.True(manager.TryRenameToken(entity.EntityId, "after-rename", "u", out var renamed));

            Assert.Equal("after-rename", renamed.Name);
            Assert.True(renamed.ReadOnly); // the rename touches nothing but the name

            // Persisted: a fresh index sees the renamed record.
            using var reopened = CreateManager();
            reopened.Initialize().Wait();

            var reloaded = reopened.GetToken(ApiTokenMaterial.TokenIdOf(fullToken));

            Assert.Equal("after-rename", reloaded.Name);
            Assert.True(reloaded.ReadOnly);
        }


        [Fact]
        public void TryRenameToken_NoOpRequest_SucceedsWithoutRewrite()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            manager.TryCreateToken(OwnerId, "no-op", readOnly: false, "u", out var entity, out _);

            Assert.True(manager.TryRenameToken(entity.EntityId, "no-op", "u", out var unchanged));

            Assert.Equal("no-op", unchanged.Name);
            Assert.Equal(entity.CreatedAtUtc, unchanged.CreatedAtUtc);
        }


        [Fact]
        public void TryRenameToken_EmptyName_IsRejected()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            manager.TryCreateToken(OwnerId, "keep-my-name", readOnly: false, "u", out var entity, out var fullToken);

            Assert.False(manager.TryRenameToken(entity.EntityId, "   ", "u", out _));
            Assert.Equal("keep-my-name", manager.GetToken(ApiTokenMaterial.TokenIdOf(fullToken)).Name);
        }


        [Fact]
        public void TryRenameToken_RevokedToken_IsRejected()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            manager.TryCreateToken(OwnerId, "dead", readOnly: false, "u", out var entity, out var fullToken);
            manager.TryRevokeToken(entity.EntityId, "u", "gone", out _);

            Assert.False(manager.TryRenameToken(entity.EntityId, "zombie", "u", out _));
            Assert.Equal("dead", manager.GetToken(ApiTokenMaterial.TokenIdOf(fullToken)).Name);
        }


        [Fact]
        public void TryRenameToken_AfterEmergencyRevoke_IsRejected()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            manager.TryCreateToken(OwnerId, "generation-dead", readOnly: false, "u", out var entity, out var fullToken);

            // Emergency revoke advances the generation; the record keeps RevokedAtUtc == null.
            manager.AdvanceOwnerRevocationGeneration(OwnerId);

            Assert.False(manager.TryRenameToken(entity.EntityId, "zombie", "u", out _));
            Assert.Equal("generation-dead", manager.GetToken(ApiTokenMaterial.TokenIdOf(fullToken)).Name);
        }


        [Fact]
        public void TryRotateToken_RevokesOldIssuesFreshPairAndPreservesThePowerProfile()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            manager.TryCreateToken(OwnerId, "to-rotate", readOnly: true, "u", out var old, out var oldFullToken);

            Assert.True(manager.TryRotateToken(old.EntityId, "rotating-user", out var replacement, out var newFullToken));

            // Completely fresh identifiers: no value from the old token is reused.
            Assert.NotEqual(old.EntityId, replacement.EntityId);
            Assert.NotEqual(ApiTokenMaterial.TokenIdOf(oldFullToken), ApiTokenMaterial.TokenIdOf(newFullToken));
            Assert.NotEqual(oldFullToken, newFullToken);
            Assert.Equal(old.EntityId, replacement.RotatedFromEntityId);
            Assert.NotNull(replacement.RotatedAtUtc);

            // Audit trail: the original creator survives rotation, the rotating actor is
            // recorded separately — once retention removes the source row, the lineage
            // must still answer "who minted this" and "who rotated it".
            Assert.Equal(old.CreatedBy, replacement.CreatedBy);
            Assert.Equal("rotating-user", replacement.RotatedBy);

            // Rotation is a pure credential swap (#1384): the name and the read-only
            // flag carry over unchanged.
            Assert.Equal(old.Name, replacement.Name);
            Assert.Equal(old.ReadOnly, replacement.ReadOnly);
            Assert.True(replacement.ReadOnly);

            // Old token is revoked immediately, new one authenticates on lookup.
            Assert.NotNull(manager.GetToken(ApiTokenMaterial.TokenIdOf(oldFullToken)).RevokedAtUtc);
            Assert.Null(manager.GetToken(ApiTokenMaterial.TokenIdOf(newFullToken)).RevokedAtUtc);

            // The replacement takes the source slot: still one quota-eligible token.
            Assert.Equal(1, manager.CountQuotaEligibleTokens(OwnerId));
        }


        [Fact]
        public void TryRotateToken_AfterEmergencyRevoke_IsRefused()
        {
            // An emergency revoke kills records by advancing a generation, leaving
            // RevokedAtUtc null. Rotating such a record must not mint a live replacement
            // stamped with the current generation — that would silently undo the revoke.
            using var manager = CreateManager();
            manager.Initialize().Wait();

            manager.TryCreateToken(OwnerId, "global-killed", readOnly: false, "u", out var globalKilled, out var globalKilledToken);

            manager.AdvanceGlobalRevocationGeneration();

            Assert.False(manager.TryRotateToken(globalKilled.EntityId, "u", out _, out _));
            Assert.Null(manager.GetToken(ApiTokenMaterial.TokenIdOf(globalKilledToken)).RevokedAtUtc);
            Assert.Single(manager.GetTokensByOwner(OwnerId));
            Assert.Equal(0, manager.CountQuotaEligibleTokens(OwnerId));

            // The owner-scoped emergency revoke is refused the same way.
            manager.TryCreateToken(OwnerId, "owner-killed", readOnly: false, "u", out var ownerKilled, out var ownerKilledToken);

            manager.AdvanceOwnerRevocationGeneration(OwnerId);

            Assert.False(manager.TryRotateToken(ownerKilled.EntityId, "u", out _, out _));
            Assert.Null(manager.GetToken(ApiTokenMaterial.TokenIdOf(ownerKilledToken)).RevokedAtUtc);
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

            manager.TryCreateToken(OwnerId, "one", readOnly: false, "u", out _, out _);
            manager.TryCreateToken(OwnerId, "two", readOnly: false, "u", out _, out _);

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

            manager.TryCreateToken(OwnerId, "mine", readOnly: false, "u", out _, out _);
            manager.TryCreateToken(otherOwner, "theirs", readOnly: false, "u", out _, out _);

            Assert.Equal(1, manager.AdvanceOwnerRevocationGeneration(OwnerId));

            Assert.Equal(0, manager.CountQuotaEligibleTokens(OwnerId));
            Assert.Equal(1, manager.CountQuotaEligibleTokens(otherOwner));
        }


        [Fact]
        public void Initialize_RegressedGenerationState_FailsClosed()
        {
            // A record issued at a generation newer than the authoritative one can only mean
            // damaged generation storage: the whole index must fail closed.
            _databaseCoreManager.DatabaseCore.PutApiToken(BuildRow(name: "from-the-future") with
            {
                GlobalRevocationGenerationAtIssue = 5,
                OwnerRevocationGenerationAtIssue = 0,
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
            _databaseCoreManager.DatabaseCore.PutApiToken(BuildRow(tokenId: "short", name: "corrupt"));

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

            Assert.False(manager.TryCreateToken(OwnerId, "doomed", readOnly: false, "u", out _, out _));
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

            manager.TryCreateToken(OwnerId, "stays-active", readOnly: false, "u", out var entity, out var fullToken);

            var failing = new HSMServer.Core.Tests.Infrastructure.FailingDatabaseCore(_databaseCoreManager.DatabaseCore, _ => false)
            {
                ShouldFailApiTokenOp = op => op == "PutApiToken",
            };

            using var failingManager = new ApiTokenManager(failing, NullLogger<ApiTokenManager>.Instance);
            failingManager.Initialize().Wait();

            Assert.False(failingManager.TryRevokeToken(entity.EntityId, "u", null, out _));
            Assert.Null(failingManager.GetToken(ApiTokenMaterial.TokenIdOf(fullToken)).RevokedAtUtc);
        }


        [Fact]
        public void TryRotateToken_WriteFailure_SourceTokenUnchanged()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            manager.TryCreateToken(OwnerId, "no-rotation", readOnly: false, "u", out var entity, out var fullToken);

            var failing = new HSMServer.Core.Tests.Infrastructure.FailingDatabaseCore(_databaseCoreManager.DatabaseCore, _ => false)
            {
                ShouldFailApiTokenOp = op => op == "TryRotateApiToken",
            };

            using var failingManager = new ApiTokenManager(failing, NullLogger<ApiTokenManager>.Instance);
            failingManager.Initialize().Wait();

            Assert.False(failingManager.TryRotateToken(entity.EntityId, "u", out _, out _));
            Assert.Null(failingManager.GetToken(ApiTokenMaterial.TokenIdOf(fullToken)).RevokedAtUtc);
            Assert.Equal(1, failingManager.CountQuotaEligibleTokens(OwnerId));
        }


        [Fact]
        public void TryCreateToken_SanitizesAndBoundsFreeText()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            // Over-length name is REJECTED, not silently shortened: an
            // operator's token must not be named something other than what they typed.
            Assert.False(manager.TryCreateToken(OwnerId, new string('n', 512), readOnly: false, "u", out _, out _));

            // Within the bounds, control characters are neutralized.
            Assert.True(manager.TryCreateToken(OwnerId, "bounded", readOnly: false, "u", out var entity, out _));

            // Actor fields get the same treatment as free text.
            Assert.True(manager.TryCreateToken(OwnerId, "actor-sanitize", readOnly: false,
                "attacker\r\nadmin", out var actorEntity, out _));

            Assert.Equal("attacker  admin", actorEntity.CreatedBy);

            // The revocation reason and the revoking actor are sanitized the same way:
            // each control character becomes one space, so nothing can forge log lines.
            manager.TryRevokeToken(entity.EntityId, "attacker\x0000", "forged\r\nsecond line", out var revoked);

            Assert.Equal("attacker", revoked.RevokedBy);
            Assert.Equal("forged  second line", revoked.RevocationReason);
        }


        [Fact]
        public void ActorFieldTruncation_NeverSplitsASurrogatePairAndNeverEndsInAReplacedSpace()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            // Name over-length is rejected outright, so bounded truncation applies to
            // the actor fields: 255 'n' + a 2-char surrogate pair cuts at the pair's
            // high half — the cut must back off to 255 and leave no lone surrogate.
            manager.TryCreateToken(OwnerId, "surrogate-cut", readOnly: false,
                $"{new string('n', 255)}\U0001F600", out var surrogateEntity, out _);

            Assert.Equal(255, surrogateEntity.CreatedBy.Length);
            Assert.All(surrogateEntity.CreatedBy, c => Assert.False(char.IsSurrogate(c)));

            // 255 'n', a NUL (becomes a space at index 255), then a tail: the 256-char cut
            // lands right after the replaced space, and the result must re-trim it.
            manager.TryCreateToken(OwnerId, "space-cut", readOnly: false,
                $"{new string('n', 255)}\0tail", out var spaceEntity, out _);

            Assert.Equal(255, spaceEntity.CreatedBy.Length);
            Assert.Equal(new string('n', 255), spaceEntity.CreatedBy);
        }


        [Fact]
        public void TryCreateToken_UnpairedSurrogate_IsReplacedLikeTheJsonRoundTripWould()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            // A lone surrogate would become U+FFFD only in the durable row; replacing it
            // during sanitization keeps the live entity and the row identical.
            manager.TryCreateToken(OwnerId, "lone\uD800high", readOnly: false, "u", out var entity, out var fullToken);

            Assert.Equal("lone�high", entity.Name);

            using var reopened = CreateManager();
            reopened.Initialize().Wait();

            Assert.Equal(entity.Name, reopened.GetToken(ApiTokenMaterial.TokenIdOf(fullToken)).Name);
        }


        [Fact]
        public void TryCreateToken_ControlOnlyFreeText_NormalizesToNull()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            // Input that sanitizes to nothing must have exactly one persisted shape: null.
            manager.TryCreateToken(OwnerId, "null-shapes", readOnly: false, "\t", out var entity, out _);

            Assert.Null(entity.CreatedBy);
        }


        [Fact]
        public void NullGrantsJsonRow_CannotBecomeALoadableRecord()
        {
            // The entity type no longer admits a null/default grant array through
            // persistence (System.Text.Json refuses to serialize a default
            // ImmutableArray), so a grants-less row can only exist as hand-written
            // JSON. Fail-closed either way: the deserializer rejects it, or it lands
            // as a default array that IsLoadable refuses to publish.
            var rowJson = $$"""
                {
                  "EntityVersion": 1,
                  "EntityId": "{{Guid.NewGuid()}}",
                  "TokenId": "{{new string('A', ApiTokenMaterial.TokenIdLength)}}",
                  "VersionByte": 1,
                  "Verifier": "{{new string('A', 43)}}",
                  "OwnerUserId": "{{OwnerId}}",
                  "Name": "null-grants-row",
                  "Grants": null
                }
                """;

            ApiTokenEntity entity = null;

            try
            {
                entity = System.Text.Json.JsonSerializer.Deserialize<ApiTokenEntity>(rowJson);
            }
            catch (System.Text.Json.JsonException)
            {
                // Rejected at the deserializer — already fail closed.
            }

            Assert.True(entity is null || entity.Grants.IsDefault);
        }


        [Fact]
        public void TryRemoveToken_RemovesDurableRowAndLiveIndexTogether()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            manager.TryCreateToken(OwnerId, "to-remove", readOnly: false, "u", out var entity, out var fullToken);

            Assert.True(manager.TryRemoveToken(ApiTokenMaterial.TokenIdOf(fullToken)));

            Assert.Null(manager.GetToken(ApiTokenMaterial.TokenIdOf(fullToken)));
            Assert.Null(manager.GetTokenByEntityId(entity.EntityId));
            Assert.Empty(manager.GetTokensByOwner(OwnerId));
            Assert.Equal(0, manager.CountQuotaEligibleTokens(OwnerId));

            // The durable row is gone too: a fresh index does not resurrect it.
            using var reopened = CreateManager();
            reopened.Initialize().Wait();

            Assert.Null(reopened.GetToken(ApiTokenMaterial.TokenIdOf(fullToken)));

            // Idempotent: the durable row is gone either way — true means "gone", and an
            // absent row is as gone as a deleted one. Only a null id reports false.
            Assert.True(manager.TryRemoveToken(ApiTokenMaterial.TokenIdOf(fullToken)));
            Assert.False(manager.TryRemoveToken(null));
        }


        [Fact]
        public void TryRemoveToken_FailedDurableRemoval_UnpublishesNothing()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            manager.TryCreateToken(OwnerId, "keep-on-failure", readOnly: false, "u", out _, out var fullToken);

            var failing = new HSMServer.Core.Tests.Infrastructure.FailingDatabaseCore(_databaseCoreManager.DatabaseCore, _ => false)
            {
                ShouldFailApiTokenOp = op => op == "RemoveApiToken",
            };

            using var failingManager = new ApiTokenManager(failing, NullLogger<ApiTokenManager>.Instance);
            failingManager.Initialize().Wait();

            // The durable row may still exist — the live record must stay published.
            Assert.False(failingManager.TryRemoveToken(ApiTokenMaterial.TokenIdOf(fullToken)));

            Assert.NotNull(failingManager.GetToken(ApiTokenMaterial.TokenIdOf(fullToken)));
            Assert.Single(failingManager.GetTokensByOwner(OwnerId));
        }


        [Fact]
        public void TryRemoveToken_OrphanRowRejectedAtLoad_IsStillRemovedDurably()
        {
            // Rows rejected by IsLoadable never enter the live index — without the
            // durable-delete fallback they would be un-collectable forever, rescanned and
            // re-warned at every boot. Retention must be able to clear them.
            var orphanTokenId = new string('Q', ApiTokenMaterial.TokenIdLength);

            _databaseCoreManager.DatabaseCore.PutApiToken(BuildRow(tokenId: orphanTokenId, name: "future-version-orphan") with
            {
                EntityVersion = 2, // future version: rejected at load
            });

            using var manager = CreateManager();
            manager.Initialize().Wait();

            Assert.Null(manager.GetToken(orphanTokenId));

            Assert.True(manager.TryRemoveToken(orphanTokenId));

            // Gone durably: the next boot does not rescan it.
            using var reopened = CreateManager();
            reopened.Initialize().Wait();

            Assert.Null(_databaseCoreManager.DatabaseCore.GetApiToken(orphanTokenId));
        }


        [Fact]
        public void Initialize_RowWhoseKeyDisagreesWithItsTokenId_IsSkippedNotRepublished()
        {
            // A row stored under ApiToken_K whose payload says TokenId = T would be
            // published under T; every lifecycle write then targets ApiToken_T while the
            // stale ApiToken_K row is rescanned and re-published at each restart,
            // silently undoing the revocation once per restart. The loader must reject
            // the row (and log the offending key so retention can clear it).
            var row = BuildRow(tokenId: new string('Q', ApiTokenMaterial.TokenIdLength), name: "key-payload-mismatch");

            var failing = new HSMServer.Core.Tests.Infrastructure.FailingDatabaseCore(_databaseCoreManager.DatabaseCore, _ => false)
            {
                OverrideApiTokenScan = () => [(new string('A', ApiTokenMaterial.TokenIdLength), row)],
            };

            using var manager = new ApiTokenManager(failing, NullLogger<ApiTokenManager>.Instance);

            manager.Initialize().Wait();

            // A rejected row is a skip, not an outage: the rest of the index stays healthy.
            Assert.True(manager.IsGenerationStateHealthy);
            Assert.Null(manager.GetToken(row.TokenId));
            Assert.Null(manager.GetTokenByEntityId(row.EntityId));
            Assert.Empty(manager.GetTokensByOwner(OwnerId));
        }


        [Fact]
        public void Initialize_RejectedRows_AreRegisteredAsOrphans_ForRetention()
        {
            // Rows rejected at load are the orphans the retention sweep exists to clear:
            // the registry names the STORAGE key (for a key/payload mismatch that is the
            // key's id, not the payload's), and TryRemoveToken prunes the entry once the
            // row is gone.
            var row = BuildRow(tokenId: new string('Q', ApiTokenMaterial.TokenIdLength), name: "key-payload-mismatch");

            var orphanKey = new string('A', ApiTokenMaterial.TokenIdLength);

            var failing = new HSMServer.Core.Tests.Infrastructure.FailingDatabaseCore(_databaseCoreManager.DatabaseCore, _ => false)
            {
                OverrideApiTokenScan = () => [(orphanKey, row)],
            };

            using var manager = new ApiTokenManager(failing, NullLogger<ApiTokenManager>.Instance);

            manager.Initialize().Wait();

            Assert.Equal(new[] { orphanKey }, manager.GetOrphanTokenIds());
            Assert.True(manager.TryRemoveToken(orphanKey));
            Assert.Empty(manager.GetOrphanTokenIds());
        }


        [Fact]
        public void Initialize_DuplicateEntityIdRows_OnlyTheFirstIsPublished()
        {
            // Two rows sharing an EntityId would shadow each other in the entity-id map:
            // a revoke-by-entity-id would report success while the shadowed token keeps
            // authenticating. The later row must be skipped fail-closed.
            var sharedEntityId = Guid.NewGuid();
            var firstTokenId = new string('A', ApiTokenMaterial.TokenIdLength);
            var secondTokenId = new string('Q', ApiTokenMaterial.TokenIdLength);

            for (var i = 0; i < 2; i++)
            {
                _databaseCoreManager.DatabaseCore.PutApiToken(BuildRow(entityId: sharedEntityId,
                    tokenId: i == 0 ? firstTokenId : secondTokenId, name: $"duplicate-{i}"));
            }

            using var manager = CreateManager();
            manager.Initialize().Wait();

            // Exactly one of the two is live, and no revoke-by-entity-id can leave a
            // shadowed authenticating record behind.
            var liveCount = new[] { firstTokenId, secondTokenId }.Count(id => manager.GetToken(id) is not null);

            Assert.Equal(1, liveCount);
            Assert.Single(manager.GetTokensByOwner(OwnerId));
            Assert.NotNull(manager.GetTokenByEntityId(sharedEntityId));
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

            Assert.False(manager.TryCreateToken(OwnerId, "doomed", readOnly: false, "u", out _, out _));
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

            manager.TryCreateToken(OwnerId, "no-rotate-unhealthy", readOnly: false, "u", out var entity, out var fullToken);

            var failing = new HSMServer.Core.Tests.Infrastructure.FailingDatabaseCore(_databaseCoreManager.DatabaseCore, _ => false)
            {
                ShouldFailApiTokenOp = op => op == "GetGlobalRevocationGeneration",
            };

            using var failingManager = new ApiTokenManager(failing, NullLogger<ApiTokenManager>.Instance);
            failingManager.Initialize().Wait();

            Assert.False(failingManager.IsGenerationStateHealthy);
            Assert.False(failingManager.TryRotateToken(entity.EntityId, "u", out _, out _));
            Assert.Null(failingManager.GetToken(ApiTokenMaterial.TokenIdOf(fullToken)).RevokedAtUtc);
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

            Assert.False(manager.TryCreateToken(Guid.NewGuid(), "corrupt-owner-generation", readOnly: false, "u", out _, out _));
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

            Assert.True(manager.TryCreateToken(orphanOwner, "post-cleanup", readOnly: false, "u", out var entity, out _));
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
                .Select(i => Task.Run(() => manager.TryCreateToken(OwnerId, $"parallel-{i}", readOnly: false, "u", out _, out _)))
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
        public void ConcurrentRevokeVersusRename_RevocationIsNeverLost()
        {
            using var manager = CreateManager();
            manager.Initialize().Wait();

            var tokenIds = new List<string>();

            for (var round = 0; round < 20; round++)
            {
                manager.TryCreateToken(OwnerId, $"race-rename-{round}", readOnly: false, "u", out var entity, out var fullToken);
                tokenIds.Add(ApiTokenMaterial.TokenIdOf(fullToken));

                using var start = new Barrier(2);

                var revoking = Task.Run(() =>
                {
                    start.SignalAndWait();
                    return manager.TryRevokeToken(entity.EntityId, "u", "race", out _);
                });
                var renaming = Task.Run(() =>
                {
                    start.SignalAndWait();
                    return manager.TryRenameToken(entity.EntityId, $"renamed-{round}", "u", out _);
                });

                Task.WaitAll(revoking, renaming);

                // Whoever wins, the revocation must survive the concurrent rename.
                Assert.True(revoking.Result);
                Assert.NotNull(manager.GetToken(ApiTokenMaterial.TokenIdOf(fullToken)).RevokedAtUtc);
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
                manager.TryCreateToken(OwnerId, $"race-rotate-{round}", readOnly: false, "u", out var entity, out var fullToken);
                tokenIds.Add(ApiTokenMaterial.TokenIdOf(fullToken));

                using var start = new Barrier(2);

                var revoking = Task.Run(() =>
                {
                    start.SignalAndWait();
                    return manager.TryRevokeToken(entity.EntityId, "u", "race", out _);
                });
                var rotating = Task.Run(() =>
                {
                    start.SignalAndWait();
                    return manager.TryRotateToken(entity.EntityId, "u", out _, out _);
                });

                Task.WaitAll(revoking, rotating);

                Assert.True(revoking.Result);
                Assert.NotNull(manager.GetToken(ApiTokenMaterial.TokenIdOf(fullToken)).RevokedAtUtc);
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


        // A valid new-model durable row: empty grants, no expiry — the only shape the
        // #1384 loader publishes.
        private static ApiTokenEntity BuildRow(Guid? entityId = null, string tokenId = null, string name = "row") => new()
        {
            EntityVersion = 1,
            EntityId = entityId ?? Guid.NewGuid(),
            TokenId = tokenId ?? new string('A', ApiTokenMaterial.TokenIdLength),
            VersionByte = ApiTokenMaterial.CurrentVersionByte,
            Verifier = new byte[32],
            OwnerUserId = OwnerId,
            Name = name,
            Grants = [],
            ReadOnly = false,
            CreatedAtUtc = DateTime.UtcNow.Ticks,
        };


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
