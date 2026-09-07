using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.Controllers;
using HSMServer.Core.Cache;
using HSMServer.Folders;
using HSMServer.Model.Authentication;
using HSMServer.Model.Profile;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Controllers
{
    // Cookie-only token-management endpoints of the profile page (#1356 step 4): the
    // degraded-mode gates (kill switch, unhealthy generations, quota), the create
    // validation matrix (at least one grant, grantable pairs only, no duplicates, future
    // expiry, gated no-expiration), the one-time secret contract, and the
    // indistinguishable not-found answer for foreign/dead entity ids.
    public class ProfileControllerTests
    {
        private static readonly Guid OwnerId = Guid.NewGuid();
        private static readonly Guid ForeignOwnerId = Guid.NewGuid();
        private static readonly Guid EntityId = Guid.NewGuid();
        private static readonly Guid ProductA = Guid.NewGuid();

        private readonly Mock<IApiTokenManager> _tokens = new();
        private readonly Mock<IApiTokenGrantOptionsService> _grantOptions = new();
        private readonly Mock<IUserManager> _users = new();
        private readonly Mock<ITreeValuesCache> _cache = new();
        private readonly Mock<IFolderManager> _folders = new();
        private readonly ApiTokensConfig _config = new() { Enabled = true };

        private readonly User _user = new("owner") { Id = OwnerId };

        // Captured out-arguments of the manager lifecycle calls.
        private ApiTokenInfo _createdInfo;
        private string _createdToken;


        public ProfileControllerTests()
        {
            _users.Setup(u => u[OwnerId]).Returns(_user);
            _tokens.Setup(t => t.GetTokensByOwner(OwnerId)).Returns(new List<ApiTokenInfo>());

            _tokens.Setup(t => t.IsGenerationStateHealthy).Returns(true);
            _tokens.Setup(t => t.CountQuotaEligibleTokens(OwnerId)).Returns(0);

            _grantOptions.Setup(g => g.IsGrantableByOwner(It.IsAny<User>(), It.IsAny<string>(),
                    It.IsAny<ApiTokenBoundaryKind>(), It.IsAny<string>()))
                .Returns(true);

            _tokens.Setup(t => t.TryCreateToken(OwnerId, It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<List<ApiTokenGrantEntity>>(), It.IsAny<DateTime?>(), It.IsAny<string>(),
                    out It.Ref<ApiTokenInfo>.IsAny, out It.Ref<string>.IsAny))
                .Callback(new CreateTokenCallback((Guid _, string __, string ___, List<ApiTokenGrantEntity> ____,
                    DateTime? _____, string ______, out ApiTokenInfo info, out string token) =>
                {
                    info = BuildInfo();
                    token = "hsm_pat_v1_fulltoken";
                    _createdInfo = info;
                    _createdToken = token;
                }))
                .Returns(true);
        }


        private ProfileController CreateController() =>
            new(_users.Object, _tokens.Object, _grantOptions.Object, _config, _cache.Object, _folders.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = _user },
                },
            };


        // ---- create -------------------------------------------------------------

        [Fact]
        public void CreateToken_Valid_ReturnsSecretExactlyOnce()
        {
            var answer = Mutate(CreateController().CreateToken(BuildCreateRequest()));

            Assert.True(answer.Ok);
            Assert.Equal("hsm_pat_v1_fulltoken", answer.Token);
            Assert.Equal(EntityId, answer.EntityId);
        }


        [Fact]
        public void CreateToken_Disabled_DeniedWithoutManagerCall()
        {
            _config.Enabled = false;

            var answer = Mutate(CreateController().CreateToken(BuildCreateRequest()));

            Assert.False(answer.Ok);
            Assert.Equal("disabled", answer.Error);
            _tokens.Verify(t => t.TryCreateToken(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<List<ApiTokenGrantEntity>>(), It.IsAny<DateTime?>(), It.IsAny<string>(),
                out It.Ref<ApiTokenInfo>.IsAny, out It.Ref<string>.IsAny), Times.Never);
        }


        [Fact]
        public void CreateToken_UnhealthyGenerations_Denied()
        {
            _tokens.Setup(t => t.IsGenerationStateHealthy).Returns(false);

            var answer = Mutate(CreateController().CreateToken(BuildCreateRequest()));

            Assert.False(answer.Ok);
            Assert.Equal("unhealthy", answer.Error);
        }


        [Fact]
        public void CreateToken_AtQuota_Denied()
        {
            _config.MaxTokensPerUser = 10;
            _tokens.Setup(t => t.CountQuotaEligibleTokens(OwnerId)).Returns(10);

            var answer = Mutate(CreateController().CreateToken(BuildCreateRequest()));

            Assert.False(answer.Ok);
            Assert.Equal("quota", answer.Error);
        }


        [Fact]
        public void CreateToken_NoGrants_Denied()
        {
            var request = BuildCreateRequest();
            request.Grants = new List<ProfileGrantRequest>();

            var answer = Mutate(CreateController().CreateToken(request));

            Assert.False(answer.Ok);
            Assert.Equal("no_grants", answer.Error);
        }


        [Fact]
        public void CreateToken_EmptyName_Denied()
        {
            var request = BuildCreateRequest();
            request.Name = "   ";

            var answer = Mutate(CreateController().CreateToken(request));

            Assert.False(answer.Ok);
            Assert.Equal("invalid_name", answer.Error);
        }


        [Fact]
        public void CreateToken_PastExpiry_Denied()
        {
            var request = BuildCreateRequest();
            request.ExpiresAtUtc = DateTime.UtcNow.AddDays(-1);

            var answer = Mutate(CreateController().CreateToken(request));

            Assert.False(answer.Ok);
            Assert.Equal("past_expiry", answer.Error);
        }


        [Fact]
        public void CreateToken_NoExpiration_RequiresConfigSwitch()
        {
            var request = BuildCreateRequest();
            request.ExpiresAtUtc = null;

            var answer = Mutate(CreateController().CreateToken(request));
            Assert.False(answer.Ok);
            Assert.Equal("no_expiration_not_allowed", answer.Error);

            _config.AllowNoExpiration = true;

            var allowed = Mutate(CreateController().CreateToken(request));
            Assert.True(allowed.Ok);
        }


        [Fact]
        public void CreateToken_DuplicatePairs_Denied()
        {
            var request = BuildCreateRequest();
            request.Grants.Add(new ProfileGrantRequest
            {
                Operation = ApiTokenOperations.AlertsRead,
                BoundaryKind = "product",
                BoundaryId = ProductA.ToString().ToUpperInvariant(),
            });

            var answer = Mutate(CreateController().CreateToken(request));

            // Same pair in a different Guid casing is still the same boundary.
            Assert.False(answer.Ok);
            Assert.Equal("duplicate_grant", answer.Error);
        }


        [Fact]
        public void CreateToken_GrantOutsideOwnerRights_Denied()
        {
            _grantOptions.Setup(g => g.IsGrantableByOwner(It.IsAny<User>(), ApiTokenOperations.AlertsWrite,
                    ApiTokenBoundaryKind.Product, ProductA.ToString()))
                .Returns(false);

            var request = BuildCreateRequest();
            request.Grants.Add(new ProfileGrantRequest
            {
                Operation = ApiTokenOperations.AlertsWrite,
                BoundaryKind = "product",
                BoundaryId = ProductA.ToString(),
            });

            var answer = Mutate(CreateController().CreateToken(request));

            Assert.False(answer.Ok);
            Assert.Equal("grant_not_allowed", answer.Error);
        }


        [Fact]
        public void CreateToken_ManagerFailure_Reported()
        {
            _tokens.Setup(t => t.TryCreateToken(OwnerId, It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<List<ApiTokenGrantEntity>>(), It.IsAny<DateTime?>(), It.IsAny<string>(),
                    out It.Ref<ApiTokenInfo>.IsAny, out It.Ref<string>.IsAny))
                .Returns(false);

            var answer = Mutate(CreateController().CreateToken(BuildCreateRequest()));

            Assert.False(answer.Ok);
            Assert.Equal("create_failed", answer.Error);
        }


        // ---- restrict / rotate ----------------------------------------------------

        [Fact]
        public void RestrictToken_Valid_CallsManagerWithRemainingSet()
        {
            List<ApiTokenGrantEntity> persisted = null;
            _tokens.Setup(t => t.GetTokenByEntityId(EntityId)).Returns(BuildInfo());
            _tokens.Setup(t => t.TryRestrictToken(EntityId, It.IsAny<List<ApiTokenGrantEntity>>(),
                    It.IsAny<DateTime?>(), It.IsAny<string>(), out It.Ref<ApiTokenInfo>.IsAny))
                .Callback(new RestrictCallback((Guid _, List<ApiTokenGrantEntity> grants, DateTime? expiry,
                    string ___, out ApiTokenInfo ____) =>
                {
                    persisted = grants;
                    ____ = null;
                }))
                .Returns(true);

            var answer = Mutate(CreateController().RestrictToken(new RestrictTokenRequest
            {
                EntityId = EntityId,
                Grants = new List<ProfileGrantRequest>(BuildGrants()),
            }));

            Assert.True(answer.Ok);
            var grant = Assert.Single(persisted);
            Assert.Equal(ApiTokenOperations.AlertsRead, grant.Operation);
            Assert.Equal(ProductA.ToString(), grant.BoundaryId);
        }


        [Fact]
        public void RestrictToken_ForeignToken_IndistinguishableNotFound()
        {
            _tokens.Setup(t => t.GetTokenByEntityId(EntityId)).Returns(BuildInfo(ForeignOwnerId));

            var answer = Mutate(CreateController().RestrictToken(new RestrictTokenRequest
            {
                EntityId = EntityId,
                Grants = new List<ProfileGrantRequest>(BuildGrants()),
            }));

            Assert.False(answer.Ok);
            Assert.Equal("not_found", answer.Error);
            _tokens.Verify(t => t.TryRestrictToken(It.IsAny<Guid>(), It.IsAny<List<ApiTokenGrantEntity>>(),
                It.IsAny<DateTime?>(), It.IsAny<string>(), out It.Ref<ApiTokenInfo>.IsAny), Times.Never);
        }


        [Fact]
        public void RestrictToken_RevokedToken_NotFound()
        {
            _tokens.Setup(t => t.GetTokenByEntityId(EntityId))
                .Returns(BuildInfo() with { RevokedAtUtc = DateTime.UtcNow.Ticks });

            var answer = Mutate(CreateController().RestrictToken(new RestrictTokenRequest
            {
                EntityId = EntityId,
                Grants = new List<ProfileGrantRequest>(BuildGrants()),
            }));

            Assert.False(answer.Ok);
            Assert.Equal("not_found", answer.Error);
        }


        [Fact]
        public void RestrictToken_Disabled_Denied()
        {
            _config.Enabled = false;
            _tokens.Setup(t => t.GetTokenByEntityId(EntityId)).Returns(BuildInfo());

            var answer = Mutate(CreateController().RestrictToken(new RestrictTokenRequest
            {
                EntityId = EntityId,
                Grants = new List<ProfileGrantRequest>(BuildGrants()),
            }));

            Assert.Equal("disabled", answer.Error);
        }


        [Fact]
        public void RestrictToken_GenerationInvalidatedToken_NotFound()
        {
            // The liveness rule must match what the page renders: a row the list shows
            // as "invalidated" (dead, no buttons) answers not_found here too, instead
            // of failing inside the manager with a misleading restrict_failed.
            _tokens.Setup(t => t.GlobalRevocationGeneration).Returns(5);
            _tokens.Setup(t => t.GetTokenByEntityId(EntityId))
                .Returns(BuildInfo() with { GlobalRevocationGenerationAtIssue = 4 });

            var answer = Mutate(CreateController().RestrictToken(new RestrictTokenRequest
            {
                EntityId = EntityId,
                Grants = new List<ProfileGrantRequest>(BuildGrants()),
            }));

            Assert.False(answer.Ok);
            Assert.Equal("not_found", answer.Error);
            _tokens.Verify(t => t.TryRestrictToken(It.IsAny<Guid>(), It.IsAny<List<ApiTokenGrantEntity>>(),
                It.IsAny<DateTime?>(), It.IsAny<string>(), out It.Ref<ApiTokenInfo>.IsAny), Times.Never);
        }


        [Fact]
        public void RotateToken_Valid_ReturnsNewSecretOnce()
        {
            _tokens.Setup(t => t.GetTokenByEntityId(EntityId)).Returns(BuildInfo());
            _tokens.Setup(t => t.TryRotateToken(EntityId, It.IsAny<DateTime?>(), It.IsAny<string>(),
                    out It.Ref<ApiTokenInfo>.IsAny, out It.Ref<string>.IsAny))
                .Callback(new RotateCallback((Guid _, DateTime? _, string ___, out ApiTokenInfo ____, out string token) =>
                {
                    ____ = null;
                    token = "hsm_pat_v1_rotated";
                }))
                .Returns(true);

            var answer = Mutate(CreateController().RotateToken(new RotateTokenRequest { EntityId = EntityId }));

            Assert.True(answer.Ok);
            Assert.Equal("hsm_pat_v1_rotated", answer.Token);
        }


        [Fact]
        public void RotateToken_PastExpiry_Denied()
        {
            _tokens.Setup(t => t.GetTokenByEntityId(EntityId)).Returns(BuildInfo());

            var answer = Mutate(CreateController().RotateToken(new RotateTokenRequest
            {
                EntityId = EntityId,
                ExpiresAtUtc = DateTime.UtcNow.AddDays(-1),
            }));

            Assert.Equal("past_expiry", answer.Error);
        }


        // ---- revoke ---------------------------------------------------------------

        [Fact]
        public void RevokeToken_OwnToken_RevokedWithActor()
        {
            string actor = null;
            _tokens.Setup(t => t.GetTokenByEntityId(EntityId)).Returns(BuildInfo());
            _tokens.Setup(t => t.TryRevokeToken(EntityId, It.IsAny<string>(), It.IsAny<string>(),
                    out It.Ref<ApiTokenInfo>.IsAny))
                .Callback(new RevokeCallback((Guid _, string by, string reason, out ApiTokenInfo info) =>
                {
                    actor = by;
                    info = null;
                }))
                .Returns(true);

            var answer = Mutate(CreateController().RevokeToken(new RevokeTokenRequest
            {
                EntityId = EntityId,
                Reason = "leaked",
            }));

            Assert.True(answer.Ok);
            Assert.Equal("owner", actor);
        }


        [Fact]
        public void RevokeToken_WorksWithTokensDisabled()
        {
            // The kill switch keeps cookie list/revoke alive for cleanup on purpose.
            _config.Enabled = false;
            _tokens.Setup(t => t.GetTokenByEntityId(EntityId)).Returns(BuildInfo());
            _tokens.Setup(t => t.TryRevokeToken(EntityId, It.IsAny<string>(), It.IsAny<string>(),
                    out It.Ref<ApiTokenInfo>.IsAny))
                .Returns(true);

            var answer = Mutate(CreateController().RevokeToken(new RevokeTokenRequest { EntityId = EntityId }));

            Assert.True(answer.Ok);
        }


        [Fact]
        public void RevokeToken_UnhealthyGenerations_Denied()
        {
            _tokens.Setup(t => t.IsGenerationStateHealthy).Returns(false);

            var answer = Mutate(CreateController().RevokeToken(new RevokeTokenRequest { EntityId = EntityId }));

            Assert.Equal("unhealthy", answer.Error);
        }


        [Fact]
        public void RevokeToken_ForeignToken_NotFound()
        {
            _tokens.Setup(t => t.GetTokenByEntityId(EntityId)).Returns(BuildInfo(ForeignOwnerId));

            var answer = Mutate(CreateController().RevokeToken(new RevokeTokenRequest { EntityId = EntityId }));

            Assert.Equal("not_found", answer.Error);
        }


        // ---- page / picker ---------------------------------------------------------

        [Fact]
        public void Index_ListsOnlyOwnTokensWithDegradedState()
        {
            var ownInfo = BuildInfo();
            _tokens.Setup(t => t.GetTokensByOwner(OwnerId)).Returns(new List<ApiTokenInfo> { ownInfo });
            _config.MaxTokensPerUser = 5;
            _tokens.Setup(t => t.CountQuotaEligibleTokens(OwnerId)).Returns(3);

            var result = Assert.IsType<ViewResult>(CreateController().Index());
            var model = Assert.IsType<ProfilePageViewModel>(result.Model);

            Assert.Equal("owner", model.UserName);
            Assert.Equal(3, model.QuotaUsed);
            Assert.Equal(5, model.QuotaMax);
            Assert.True(model.TokensEnabled);
            var token = Assert.Single(model.Tokens);
            Assert.Equal(EntityId, token.EntityId);

            _tokens.Verify(t => t.GetTokensByOwner(OwnerId), Times.Once);
        }


        [Fact]
        public void Index_DisabledButHealthy_ReportsBoth()
        {
            _config.Enabled = false;

            var model = PageModelOf(CreateController().Index());

            Assert.False(model.TokensEnabled);
            Assert.True(model.GenerationStateHealthy);
        }


        [Fact]
        public void Index_Timestamps_AreUnixMillisecondsNotDotNetTicks()
        {
            // Entity timestamps are .NET ticks (since 0001-01-01); feeding them to
            // new Date(ms) as-is rendered dates ~2000 years in the future. The page
            // projection must be true Unix milliseconds.
            var created = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            var expires = new DateTime(2027, 1, 1, 12, 0, 0, DateTimeKind.Utc);

            _tokens.Setup(t => t.GetTokensByOwner(OwnerId)).Returns(new List<ApiTokenInfo>
            {
                BuildInfo() with { CreatedAtUtc = created.Ticks, ExpiresAtUtc = expires.Ticks },
            });

            var token = PageModelOf(CreateController().Index()).Tokens.Single();

            Assert.Equal(((DateTimeOffset)created).ToUnixTimeMilliseconds(), token.CreatedAtUnixMs);
            Assert.Equal(((DateTimeOffset)expires).ToUnixTimeMilliseconds(), token.ExpiresAtUnixMs);
        }


        [Fact]
        public void Index_GenerationInvalidatedToken_IsDeadNotActive()
        {
            // An emergency-revoke generation advance leaves both row timestamps unset;
            // without comparing the at-issue stamps the row would list as "active" with
            // live lifecycle buttons while no longer authenticating, and would disagree
            // with the quota counter that already excludes it.
            _tokens.Setup(t => t.GlobalRevocationGeneration).Returns(5);
            _tokens.Setup(t => t.GetOwnerRevocationGeneration(OwnerId)).Returns(2);
            _tokens.Setup(t => t.GetTokensByOwner(OwnerId)).Returns(new List<ApiTokenInfo>
            {
                BuildInfo(entityId: Guid.NewGuid()) with { GlobalRevocationGenerationAtIssue = 4, OwnerRevocationGenerationAtIssue = 2 },
                BuildInfo(entityId: Guid.NewGuid()) with { GlobalRevocationGenerationAtIssue = 5, OwnerRevocationGenerationAtIssue = 1 },
                BuildInfo(entityId: Guid.NewGuid()) with { GlobalRevocationGenerationAtIssue = 5, OwnerRevocationGenerationAtIssue = 2 },
            });

            var statuses = PageModelOf(CreateController().Index()).Tokens.Select(t => t.Status).ToList();

            Assert.Equal(new[] { "invalidated", "invalidated", "active" }, statuses);
        }


        [Fact]
        public void CreateToken_UnspecifiedExpiryKind_IsUtcPerManagerContract()
        {
            // The manager treats Kind.Unspecified as UTC; ToUniversalTime would instead
            // read the SERVER's zone and shift the stored expiry on non-UTC hosts.
            DateTime? received = null;
            _tokens.Setup(t => t.TryCreateToken(OwnerId, It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<List<ApiTokenGrantEntity>>(), It.IsAny<DateTime?>(), It.IsAny<string>(),
                    out It.Ref<ApiTokenInfo>.IsAny, out It.Ref<string>.IsAny))
                .Callback(new CreateTokenCallback((Guid _, string __, string ___, List<ApiTokenGrantEntity> ____,
                    DateTime? expiresAtUtc, string _____, out ApiTokenInfo info, out string token) =>
                {
                    received = expiresAtUtc;
                    info = BuildInfo();
                    token = "hsm_pat_v1_fulltoken";
                }))
                .Returns(true);

            var unspecified = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(30).Date, DateTimeKind.Unspecified);
            var request = BuildCreateRequest();
            request.ExpiresAtUtc = unspecified;

            var answer = Mutate(CreateController().CreateToken(request));

            Assert.True(answer.Ok);
            Assert.Equal(DateTimeKind.Utc, received.Value.Kind);
            Assert.Equal(unspecified, received.Value);
        }


        [Fact]
        public void GrantOptions_Disabled_ReturnsEmptyPicker()
        {
            _config.Enabled = false;

            var result = Assert.IsType<JsonResult>(CreateController().GrantOptions());
            var boundaries = Assert.IsAssignableFrom<System.Collections.Generic.IReadOnlyList<ApiTokenBoundaryOptions>>(result.Value);

            Assert.Empty(boundaries);
            _grantOptions.Verify(g => g.GetBoundaryOptions(It.IsAny<User>()), Times.Never);
        }


        [Fact]
        public void GrantOptions_Enabled_DelegatesToOwnerFilter()
        {
            var expected = new List<ApiTokenBoundaryOptions>
            {
                new("global", "", "Global", new[] { ApiTokenOperations.SystemHealthRead }),
            };
            _grantOptions.Setup(g => g.GetBoundaryOptions(_user)).Returns(expected);

            var result = Assert.IsType<JsonResult>(CreateController().GrantOptions());
            var boundaries = Assert.IsAssignableFrom<System.Collections.Generic.IReadOnlyList<ApiTokenBoundaryOptions>>(result.Value);

            Assert.Equal(expected, boundaries);
        }


        // ---- helpers ------------------------------------------------------------------

        private static ProfileGrantRequest Grant() => new()
        {
            Operation = ApiTokenOperations.AlertsRead,
            BoundaryKind = "product",
            BoundaryId = ProductA.ToString(),
        };

        private static List<ProfileGrantRequest> BuildGrants() => new() { Grant() };

        private static CreateTokenRequest BuildCreateRequest() => new()
        {
            Name = "ci runner",
            Description = "read alerts",
            ExpiresAtUtc = DateTime.UtcNow.AddDays(30),
            Grants = BuildGrants(),
        };

        private static ApiTokenInfo BuildInfo(Guid? owner = null, Guid? entityId = null) => new()
        {
            EntityId = entityId ?? EntityId,
            OwnerUserId = owner ?? OwnerId,
            Name = "token",
            Grants = ImmutableArray<ApiTokenGrantEntity>.Empty,
        };

        private static ProfilePageViewModel PageModelOf(IActionResult action) =>
            Assert.IsType<ProfilePageViewModel>(Assert.IsType<ViewResult>(action).Model);

        private static ProfileMutationResponse Mutate(IActionResult action) =>
            Assert.IsType<ProfileMutationResponse>(Assert.IsType<JsonResult>(action).Value);


        private delegate void CreateTokenCallback(Guid owner, string name, string description,
            List<ApiTokenGrantEntity> grants, DateTime? expiresAtUtc, string createdBy,
            out ApiTokenInfo entity, out string fullToken);

        private delegate void RestrictCallback(Guid entityId, List<ApiTokenGrantEntity> grants,
            DateTime? shortenedExpiryUtc, string restrictedBy, out ApiTokenInfo entity);

        private delegate void RotateCallback(Guid entityId, DateTime? shortenedExpiryUtc, string rotatedBy,
            out ApiTokenInfo entity, out string fullToken);

        private delegate void RevokeCallback(Guid entityId, string revokedBy, string reason, out ApiTokenInfo entity);
    }
}
