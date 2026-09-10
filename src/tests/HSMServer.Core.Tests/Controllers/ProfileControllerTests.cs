using System;
using System.Collections.Generic;
using System.Linq;
using HSMServer.Authentication;
using HSMServer.Controllers;
using HSMServer.Core.Cache;
using HSMServer.Model.Authentication;
using HSMServer.Model.Profile;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Controllers
{
    // Cookie-only token-management endpoints of the profile page (#1356 step 4,
    // simplified by #1384): the degraded-mode gates (kill switch, unhealthy
    // generations, quota), the create/rename validation matrix, the one-time secret
    // contract, and the indistinguishable not-found answer for foreign/dead entity ids.
    public class ProfileControllerTests
    {
        private static readonly Guid OwnerId = Guid.NewGuid();
        private static readonly Guid ForeignOwnerId = Guid.NewGuid();
        private static readonly Guid EntityId = Guid.NewGuid();

        private readonly Mock<IApiTokenManager> _tokens = new();
        private readonly Mock<IUserManager> _users = new();
        private readonly Mock<ITreeValuesCache> _cache = new();
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

            _tokens.Setup(t => t.TryCreateToken(OwnerId, It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(),
                    out It.Ref<ApiTokenInfo>.IsAny, out It.Ref<string>.IsAny))
                .Callback(new CreateTokenCallback((Guid _, string __, bool ___, string ____,
                    out ApiTokenInfo info, out string token) =>
                {
                    info = BuildInfo();
                    token = "hsm_pat_v1_fulltoken";
                    _createdInfo = info;
                    _createdToken = token;
                }))
                .Returns(true);
        }


        private ProfileController CreateController() =>
            new(_users.Object, _tokens.Object, _config, _cache.Object)
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
        public void CreateToken_ReadOnlyFlag_PassedToManagerUntouched()
        {
            // The flag is the token's whole power profile and is fixed at creation;
            // the surface must neither drop it (a read-write mint from a read-only
            // request) nor flip it.
            bool received = true;

            _tokens.Setup(t => t.TryCreateToken(OwnerId, It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(),
                    out It.Ref<ApiTokenInfo>.IsAny, out It.Ref<string>.IsAny))
                .Callback(new CreateTokenCallback((Guid _, string __, bool readOnly, string ___,
                    out ApiTokenInfo info, out string token) =>
                {
                    received = readOnly;
                    info = BuildInfo();
                    token = "hsm_pat_v1_fulltoken";
                }))
                .Returns(true);

            var request = BuildCreateRequest();
            request.ReadOnly = true;

            Assert.True(Mutate(CreateController().CreateToken(request)).Ok);
            Assert.True(received);
        }


        [Fact]
        public void CreateToken_Disabled_DeniedWithoutManagerCall()
        {
            _config.Enabled = false;

            var answer = Mutate(CreateController().CreateToken(BuildCreateRequest()));

            Assert.False(answer.Ok);
            Assert.Equal("disabled", answer.Error);
            _tokens.Verify(t => t.TryCreateToken(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<string>(), out It.Ref<ApiTokenInfo>.IsAny, out It.Ref<string>.IsAny), Times.Never);
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
        public void CreateToken_EmptyName_Denied()
        {
            var request = BuildCreateRequest();
            request.Name = "   ";

            var answer = Mutate(CreateController().CreateToken(request));

            Assert.False(answer.Ok);
            Assert.Equal("invalid_name", answer.Error);
        }


        [Fact]
        public void CreateToken_ControlOnlyName_DeniedAsInvalidName()
        {
            // The manager's Sanitize maps control characters to spaces before its own
            // empty-name rejection; without mirroring that here the answer was a bare
            // create_failed pointing at a server log that says nothing.
            var request = BuildCreateRequest();
            request.Name = "";

            var answer = Mutate(CreateController().CreateToken(request));

            Assert.False(answer.Ok);
            Assert.Equal("invalid_name", answer.Error);
        }


        [Fact]
        public void CreateToken_ManagerFailure_Reported()
        {
            _tokens.Setup(t => t.TryCreateToken(OwnerId, It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(),
                    out It.Ref<ApiTokenInfo>.IsAny, out It.Ref<string>.IsAny))
                .Returns(false);

            var answer = Mutate(CreateController().CreateToken(BuildCreateRequest()));

            Assert.False(answer.Ok);
            Assert.Equal("create_failed", answer.Error);
        }


        // ---- rename / rotate ----------------------------------------------------

        [Fact]
        public void RenameToken_Valid_CallsManagerWithNewName()
        {
            string persisted = null;
            _tokens.Setup(t => t.GetTokenByEntityId(EntityId)).Returns(BuildInfo());
            _tokens.Setup(t => t.TryRenameToken(EntityId, It.IsAny<string>(), It.IsAny<string>(),
                    out It.Ref<ApiTokenInfo>.IsAny))
                .Callback(new RenameCallback((Guid _, string name, string ___, out ApiTokenInfo ____) =>
                {
                    persisted = name;
                    ____ = null;
                }))
                .Returns(true);

            var answer = Mutate(CreateController().RenameToken(new RenameTokenRequest
            {
                EntityId = EntityId,
                Name = "renamed",
            }));

            Assert.True(answer.Ok);
            Assert.Equal("renamed", persisted);
        }


        [Fact]
        public void RenameToken_EmptyName_Denied()
        {
            _tokens.Setup(t => t.GetTokenByEntityId(EntityId)).Returns(BuildInfo());

            var answer = Mutate(CreateController().RenameToken(new RenameTokenRequest
            {
                EntityId = EntityId,
                Name = "   ",
            }));

            Assert.False(answer.Ok);
            Assert.Equal("invalid_name", answer.Error);
        }


        [Fact]
        public void RenameToken_ForeignToken_IndistinguishableNotFound()
        {
            _tokens.Setup(t => t.GetTokenByEntityId(EntityId)).Returns(BuildInfo(ForeignOwnerId));

            var answer = Mutate(CreateController().RenameToken(new RenameTokenRequest
            {
                EntityId = EntityId,
                Name = "renamed",
            }));

            Assert.False(answer.Ok);
            Assert.Equal("not_found", answer.Error);
            _tokens.Verify(t => t.TryRenameToken(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
                out It.Ref<ApiTokenInfo>.IsAny), Times.Never);
        }


        [Fact]
        public void RenameToken_RevokedToken_NotFound()
        {
            _tokens.Setup(t => t.GetTokenByEntityId(EntityId))
                .Returns(BuildInfo() with { RevokedAtUtc = DateTime.UtcNow.Ticks });

            var answer = Mutate(CreateController().RenameToken(new RenameTokenRequest
            {
                EntityId = EntityId,
                Name = "renamed",
            }));

            Assert.False(answer.Ok);
            Assert.Equal("not_found", answer.Error);
        }


        [Fact]
        public void RenameToken_Disabled_Denied()
        {
            _config.Enabled = false;
            _tokens.Setup(t => t.GetTokenByEntityId(EntityId)).Returns(BuildInfo());

            var answer = Mutate(CreateController().RenameToken(new RenameTokenRequest
            {
                EntityId = EntityId,
                Name = "renamed",
            }));

            Assert.Equal("disabled", answer.Error);
        }


        [Fact]
        public void RenameToken_GenerationInvalidatedToken_NotFound()
        {
            // The liveness rule must match what the page renders: a row the list shows
            // as "invalidated" (dead, no buttons) answers not_found here too, instead
            // of failing inside the manager with a misleading rename_failed.
            _tokens.Setup(t => t.GlobalRevocationGeneration).Returns(5);
            _tokens.Setup(t => t.GetTokenByEntityId(EntityId))
                .Returns(BuildInfo() with { GlobalRevocationGenerationAtIssue = 4 });

            var answer = Mutate(CreateController().RenameToken(new RenameTokenRequest
            {
                EntityId = EntityId,
                Name = "renamed",
            }));

            Assert.False(answer.Ok);
            Assert.Equal("not_found", answer.Error);
            _tokens.Verify(t => t.TryRenameToken(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
                out It.Ref<ApiTokenInfo>.IsAny), Times.Never);
        }


        [Fact]
        public void RotateToken_Valid_ReturnsNewSecretOnce()
        {
            _tokens.Setup(t => t.GetTokenByEntityId(EntityId)).Returns(BuildInfo());
            _tokens.Setup(t => t.TryRotateToken(EntityId, It.IsAny<string>(),
                    out It.Ref<ApiTokenInfo>.IsAny, out It.Ref<string>.IsAny))
                .Callback(new RotateCallback((Guid _, string ___, out ApiTokenInfo ____, out string token) =>
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
        public void RotateToken_ForeignToken_NotFound()
        {
            _tokens.Setup(t => t.GetTokenByEntityId(EntityId)).Returns(BuildInfo(ForeignOwnerId));

            var answer = Mutate(CreateController().RotateToken(new RotateTokenRequest { EntityId = EntityId }));

            Assert.Equal("not_found", answer.Error);
        }


        [Fact]
        public void RotateToken_RevokedToken_NotFound()
        {
            // Rotation is the one lifecycle op that mints a fresh live credential — its
            // guards are worth pinning at the controller level, symmetric with create.
            _tokens.Setup(t => t.GetTokenByEntityId(EntityId))
                .Returns(BuildInfo() with { RevokedAtUtc = DateTime.UtcNow.Ticks });

            var answer = Mutate(CreateController().RotateToken(new RotateTokenRequest { EntityId = EntityId }));

            Assert.Equal("not_found", answer.Error);
            _tokens.Verify(t => t.TryRotateToken(It.IsAny<Guid>(), It.IsAny<string>(),
                out It.Ref<ApiTokenInfo>.IsAny, out It.Ref<string>.IsAny), Times.Never);
        }


        [Fact]
        public void RotateToken_Disabled_Denied()
        {
            _config.Enabled = false;
            _tokens.Setup(t => t.GetTokenByEntityId(EntityId)).Returns(BuildInfo());

            var answer = Mutate(CreateController().RotateToken(new RotateTokenRequest { EntityId = EntityId }));

            Assert.Equal("disabled", answer.Error);
            _tokens.Verify(t => t.TryRotateToken(It.IsAny<Guid>(), It.IsAny<string>(),
                out It.Ref<ApiTokenInfo>.IsAny, out It.Ref<string>.IsAny), Times.Never);
        }


        [Fact]
        public void RotateToken_UnhealthyGenerations_Denied()
        {
            _tokens.Setup(t => t.IsGenerationStateHealthy).Returns(false);
            _tokens.Setup(t => t.GetTokenByEntityId(EntityId)).Returns(BuildInfo());

            var answer = Mutate(CreateController().RotateToken(new RotateTokenRequest { EntityId = EntityId }));

            Assert.Equal("unhealthy", answer.Error);
            _tokens.Verify(t => t.TryRotateToken(It.IsAny<Guid>(), It.IsAny<string>(),
                out It.Ref<ApiTokenInfo>.IsAny, out It.Ref<string>.IsAny), Times.Never);
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


        // ---- page -------------------------------------------------------------------

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

            _tokens.Setup(t => t.GetTokensByOwner(OwnerId)).Returns(new List<ApiTokenInfo>
            {
                BuildInfo() with { CreatedAtUtc = created.Ticks },
            });

            var token = PageModelOf(CreateController().Index()).Tokens.Single();

            Assert.Equal(((DateTimeOffset)created).ToUnixTimeMilliseconds(), token.CreatedAtUnixMs);
        }


        [Fact]
        public void Index_ReadOnlyFlag_SurfacedOnTheRow()
        {
            _tokens.Setup(t => t.GetTokensByOwner(OwnerId)).Returns(new List<ApiTokenInfo>
            {
                BuildInfo(entityId: Guid.NewGuid()) with { ReadOnly = true },
                BuildInfo(entityId: Guid.NewGuid()) with { ReadOnly = false },
            });

            var flags = PageModelOf(CreateController().Index()).Tokens.Select(t => t.ReadOnly).ToList();

            Assert.Equal(new[] { true, false }, flags);
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
                BuildInfo(entityId: Guid.NewGuid()) with { GlobalRevocationGenerationAtIssue = 5, OwnerRevocationGenerationAtIssue = 2, RevokedAtUtc = DateTime.UtcNow.Ticks },
            });

            var statuses = PageModelOf(CreateController().Index()).Tokens.Select(t => t.Status).ToList();

            Assert.Equal(new[] { "invalidated", "invalidated", "revoked" }, statuses);
        }


        // ---- helpers ------------------------------------------------------------------

        private static CreateTokenRequest BuildCreateRequest() => new()
        {
            Name = "ci runner",
            ReadOnly = false,
        };

        private static ApiTokenInfo BuildInfo(Guid? owner = null, Guid? entityId = null) => new()
        {
            EntityId = entityId ?? EntityId,
            OwnerUserId = owner ?? OwnerId,
            Name = "token",
        };

        private static ProfilePageViewModel PageModelOf(IActionResult action) =>
            Assert.IsType<ProfilePageViewModel>(Assert.IsType<ViewResult>(action).Model);

        private static ProfileMutationResponse Mutate(IActionResult action) =>
            Assert.IsType<ProfileMutationResponse>(Assert.IsType<JsonResult>(action).Value);


        private delegate void CreateTokenCallback(Guid owner, string name, bool readOnly, string createdBy,
            out ApiTokenInfo entity, out string fullToken);

        private delegate void RenameCallback(Guid entityId, string newName, string renamedBy, out ApiTokenInfo entity);

        private delegate void RotateCallback(Guid entityId, string rotatedBy,
            out ApiTokenInfo entity, out string fullToken);

        private delegate void RevokeCallback(Guid entityId, string revokedBy, string reason, out ApiTokenInfo entity);
    }
}
