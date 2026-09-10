using System;
using System.Collections.Generic;
using System.Linq;
using HSMServer.Authentication;
using HSMServer.Constants;
using HSMServer.Core.Cache;
using HSMServer.Model.Authentication;
using HSMServer.Model.Profile;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HSMServer.Controllers
{
    // Personal API-token management of the signed-in user (#1356 step 4, simplified by
    // #1384). Cookie-only by construction: BaseController carries bare [Authorize],
    // which the cookie-pinned DefaultPolicy answers — an API-token principal can never
    // reach these endpoints, matching the initiative's "no API token may call
    // token-management endpoints".
    //
    // Mutations are JSON-in/JSON-out AJAX actions with [ValidateAntiForgeryToken]; the
    // one shared answer shape (ProfileMutationResponse) carries a stable error code the
    // page script branches on and the one-time secret exactly once (create/rotate).
    //
    // Gating follows the initiative's degraded modes: Enabled=false keeps list/revoke
    // alive for cleanup but denies create/rename/rotate; an unhealthy revocation
    // generation state denies every lifecycle mutation (the operator lever then is the
    // emergency revoke, a separate admin surface).
    [Authorize]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class ProfileController : BaseController
    {
        // Surface bound, deliberately tighter than the manager's storage bound
        // (ApiTokenManager: name 256) — two independent numbers on the same field,
        // kept apart on purpose: the surface rejects long input friendly, the manager
        // bound is the durable backstop.
        public const int MaxNameLength = 200;

        private readonly IApiTokenManager _tokens;
        private readonly ApiTokensConfig _config;
        private readonly ITreeValuesCache _cache;

        public ProfileController(IUserManager userManager, IApiTokenManager tokens,
            ApiTokensConfig config, ITreeValuesCache cache) : base(userManager)
        {
            _tokens = tokens;
            _config = config;
            _cache = cache;
        }


        [HttpGet]
        public IActionResult Index()
        {
            var user = StoredUser ?? CurrentUser;
            var ownerId = CurrentUser.Id;

            return View(new ProfilePageViewModel
            {
                UserId = user.Id,
                UserName = user.Name,
                IsAdmin = user.IsAdmin,
                // The card never renders product badges for an admin, and stale roles
                // for deleted products must not surface as raw GUID badges.
                Products = user.IsAdmin ? Array.Empty<ProfileProductRoleViewModel>() : BuildProductBadges(user),
                Tokens = BuildTokenList(ownerId),
                TokensEnabled = _config.Enabled && _tokens.IsGenerationStateHealthy,
                GenerationStateHealthy = _tokens.IsGenerationStateHealthy,
                QuotaUsed = _tokens.CountQuotaEligibleTokens(ownerId),
                QuotaMax = _config.MaxTokensPerUser,
            });
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult CreateToken([FromBody] CreateTokenRequest request)
        {
            if (!_config.Enabled)
                return Disabled();
            if (!_tokens.IsGenerationStateHealthy)
                return Unhealthy();

            if (request is null)
                return Fail("invalid_request", "The request body is missing.");

            // Control characters are normalized to spaces by the manager's Sanitize
            // before the empty-name rejection there; mirror that here so a
            // control-only name gets invalid_name instead of a bare create_failed.
            var name = new string((request.Name ?? string.Empty)
                .Select(c => char.IsControl(c) ? ' ' : c)
                .ToArray())
                .Trim();
            if (name.Length is < 1 or > MaxNameLength)
                return Fail("invalid_name", $"The token name must be 1-{MaxNameLength} characters long.");

            var ownerId = CurrentUser.Id;

            // Friendly pre-check only: the hard quota bound is enforced by the manager
            // (its ApiTokens.MaxTokensPerUser) inside the same serialized create path,
            // so a concurrent request cannot slip past the cap even when both pass here.
            if (_tokens.CountQuotaEligibleTokens(ownerId) >= _config.MaxTokensPerUser)
                return Fail("quota",
                    $"The token limit is reached ({_config.MaxTokensPerUser}). Revoke an unused token to free a slot.");

            if (!_tokens.TryCreateToken(ownerId, name, request.ReadOnly, CurrentUser.Name,
                    out var created, out var fullToken))
                return Fail("create_failed",
                    "Token creation failed. Check the server log; nothing was saved.");

            return Success(new ProfileMutationResponse
            {
                Ok = true,
                Token = fullToken,
                EntityId = created.EntityId,
            });
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult RenameToken([FromBody] RenameTokenRequest request)
        {
            if (!_config.Enabled)
                return Disabled();
            if (!_tokens.IsGenerationStateHealthy)
                return Unhealthy();

            if (request is null)
                return Fail("invalid_request", "The request body is missing.");

            if (!TryResolveOwnLiveToken(request.EntityId, out var failure, out _))
                return failure;

            var name = new string((request.Name ?? string.Empty)
                .Select(c => char.IsControl(c) ? ' ' : c)
                .ToArray())
                .Trim();
            if (name.Length is < 1 or > MaxNameLength)
                return Fail("invalid_name", $"The token name must be 1-{MaxNameLength} characters long.");

            if (!_tokens.TryRenameToken(request.EntityId, name, CurrentUser.Name, out _))
                return Fail("rename_failed", "Renaming failed. Check the server log.");

            return Success(new ProfileMutationResponse { Ok = true });
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult RotateToken([FromBody] RotateTokenRequest request)
        {
            if (!_config.Enabled)
                return Disabled();
            if (!_tokens.IsGenerationStateHealthy)
                return Unhealthy();

            if (request is null)
                return Fail("invalid_request", "The request body is missing.");

            if (!TryResolveOwnLiveToken(request.EntityId, out var failure, out _))
                return failure;

            // Rotation is a pure credential swap (#1384): the name and the read-only
            // flag carry over inside the manager, and the one-time secret is handed
            // out exactly once, here.
            if (!_tokens.TryRotateToken(request.EntityId, CurrentUser.Name, out _, out var fullToken))
                return Fail("rotate_failed", "Rotation failed. Check the server log.");

            return Success(new ProfileMutationResponse { Ok = true, Token = fullToken });
        }


        // Revocation stays available with tokens disabled — cleanup is the documented
        // purpose of the kill switch's cookie surface. Idempotent for live tokens.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult RevokeToken([FromBody] RevokeTokenRequest request)
        {
            if (!_tokens.IsGenerationStateHealthy)
                return Unhealthy();

            if (request is null)
                return Fail("invalid_request", "The request body is missing.");

            // Owner check first: a foreign or unknown id gets the same answer, so the
            // endpoint cannot be used to enumerate other users' tokens.
            var token = _tokens.GetTokenByEntityId(request.EntityId);
            if (token is null || token.OwnerUserId != CurrentUser.Id)
                return Fail("not_found", "Token not found.");

            if (!_tokens.TryRevokeToken(request.EntityId, CurrentUser.Name,
                    (request.Reason ?? string.Empty).Trim(), out _))
                return Fail("revoke_failed", "Revocation failed. Check the server log.");

            return Success(new ProfileMutationResponse { Ok = true });
        }


        // Resolves the entity id to the caller's own live token; a foreign, unknown,
        // revoked or generation-invalidated id all produce the same indistinguishable
        // failure. The liveness rule is the same one the list projection renders
        // (revoked/invalidated rows show no actions).
        private bool TryResolveOwnLiveToken(Guid entityId, out IActionResult failure, out ApiTokenInfo token)
        {
            token = _tokens.GetTokenByEntityId(entityId);

            var isOwnLive = token is not null &&
                            token.OwnerUserId == CurrentUser.Id &&
                            token.RevokedAtUtc is null &&
                            IsGenerationCurrent(token, _tokens.GlobalRevocationGeneration,
                                _tokens.GetOwnerRevocationGeneration(CurrentUser.Id));

            if (isOwnLive)
            {
                failure = null;
                return true;
            }

            failure = Fail("not_found", "Token not found.");
            return false;
        }


        // != on purpose (not <): a generation ROLLBACK — e.g. a restored backup — puts
        // AtIssue above current, and IsLive rejects that just the same; the list status
        // and the endpoint liveness check must agree by construction.
        private static bool IsGenerationCurrent(ApiTokenInfo token, long globalGeneration, long ownerGeneration) =>
            token.GlobalRevocationGenerationAtIssue == globalGeneration &&
            token.OwnerRevocationGenerationAtIssue == ownerGeneration;


        private List<ProfileTokenViewModel> BuildTokenList(Guid ownerId)
        {
            var globalGeneration = _tokens.GlobalRevocationGeneration;
            var ownerGeneration = _tokens.GetOwnerRevocationGeneration(ownerId);

            return _tokens.GetTokensByOwner(ownerId)
                .OrderByDescending(t => t.CreatedAtUtc)
                .Select(t => new ProfileTokenViewModel
                {
                    EntityId = t.EntityId,
                    Name = t.Name,
                    ReadOnly = t.ReadOnly,
                    Status = DescribeStatus(t, globalGeneration, ownerGeneration),
                    CreatedAtUnixMs = ToUnixMs(t.CreatedAtUtc),
                    LastUsedAtUnixMs = t.LastUsedAtUtc is null ? null : ToUnixMs(t.LastUsedAtUtc.Value),
                })
                .ToList();
        }


        // Generation-invalidated records (an emergency revoke advanced a generation past
        // the token's at-issue stamps) have no per-row timestamp — without this check
        // they would list as "active" with live lifecycle buttons while no longer
        // authenticating, and would disagree with the quota counter that already
        // excludes them.
        private static string DescribeStatus(ApiTokenInfo token,
            long globalGeneration, long ownerGeneration)
        {
            if (token.RevokedAtUtc is not null)
                return "revoked";

            // != rather than <: a generation ROLLBACK (e.g. a restored backup) puts
            // AtIssue above current, which IsLive rejects just the same — the page must
            // not render such a row "active" with live buttons.
            if (!IsGenerationCurrent(token, globalGeneration, ownerGeneration))
                return "invalidated";

            return "active";
        }


        private List<ProfileProductRoleViewModel> BuildProductBadges(User user) =>
            user.ProductsRoles
                .GroupBy(r => r.Item1)
                .Select(g => (Id: g.Key, IsManager: g.Any(r => r.Item2 == ProductRoleEnum.ProductManager)))
                .Select(r => (_cache.TryGetProductNameById(r.Id, out var name) ? name : null, r.IsManager))
                .Where(r => r.Item1 is not null)
                .Select(r => new ProfileProductRoleViewModel(r.Item1, r.IsManager))
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();


        private static long ToUnixMs(long ticks)
        {
            // Entity timestamps are .NET ticks (since 0001-01-01); the page script wants
            // Unix milliseconds — subtract the epoch offset before scaling.
            return (ticks - DateTime.UnixEpoch.Ticks) / TimeSpan.TicksPerMillisecond;
        }


        private static IActionResult Success(ProfileMutationResponse response) => new JsonResult(response);

        private static IActionResult Fail(string error, string message) =>
            new JsonResult(new ProfileMutationResponse { Ok = false, Error = error, Message = message });

        private static IActionResult Disabled() =>
            Fail("disabled", "API tokens are disabled by the server configuration.");

        private static IActionResult Unhealthy() =>
            Fail("unhealthy",
                "Token state is unreliable after a storage problem; lifecycle operations are unavailable. Contact an administrator.");
    }
}
