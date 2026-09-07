using System;
using System.Collections.Generic;
using System.Linq;
using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.Constants;
using HSMServer.Core.Cache;
using HSMServer.Folders;
using HSMServer.Model.Authentication;
using HSMServer.Model.Profile;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HSMServer.Controllers
{
    // Personal API-token management of the signed-in user (#1356 step 4). Cookie-only by
    // construction: BaseController carries bare [Authorize], which the cookie-pinned
    // DefaultPolicy answers — an API-token principal can never reach these endpoints,
    // matching the initiative's "no API token may call token-management endpoints".
    //
    // Mutations are JSON-in/JSON-out AJAX actions with [ValidateAntiForgeryToken]; the
    // one shared answer shape (ProfileMutationResponse) carries a stable error code the
    // page script branches on and the one-time secret exactly once (create/rotate).
    //
    // Gating follows the initiative's degraded modes: Enabled=false keeps list/revoke
    // alive for cleanup but denies create/restrict/rotate; an unhealthy revocation
    // generation state denies every lifecycle mutation (the operator lever then is the
    // emergency revoke, a separate admin surface).
    [Authorize]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class ProfileController : BaseController
    {
        public const int MaxNameLength = 200;
        public const int MaxDescriptionLength = 1000;

        private readonly IApiTokenManager _tokens;
        private readonly IApiTokenGrantOptionsService _grantOptions;
        private readonly ApiTokensConfig _config;
        private readonly ITreeValuesCache _cache;
        private readonly IFolderManager _folders;

        public ProfileController(IUserManager userManager, IApiTokenManager tokens,
            IApiTokenGrantOptionsService grantOptions, ApiTokensConfig config,
            ITreeValuesCache cache, IFolderManager folders) : base(userManager)
        {
            _tokens = tokens;
            _grantOptions = grantOptions;
            _config = config;
            _cache = cache;
            _folders = folders;
        }


        [HttpGet]
        public IActionResult Index()
        {
            var user = StoredUser ?? CurrentUser;
            var ownerId = CurrentUser.Id;
            var tokens = BuildTokenList(ownerId);

            return View(new ProfilePageViewModel
            {
                UserId = user.Id,
                UserName = user.Name,
                IsAdmin = user.IsAdmin,
                Products = BuildProductBadges(user),
                Tokens = tokens,
                GrantsByTokenId = tokens.ToDictionary(t => t.EntityId.ToString(), t => t.Grants),
                TokensEnabled = _config.Enabled && _tokens.IsGenerationStateHealthy,
                GenerationStateHealthy = _tokens.IsGenerationStateHealthy,
                AllowNoExpiration = _config.AllowNoExpiration,
                QuotaUsed = _tokens.CountQuotaEligibleTokens(ownerId),
                QuotaMax = _config.MaxTokensPerUser,
                DefaultLifetimeDays = Math.Max(1, (int)_config.DefaultLifetime.TotalDays),
            });
        }


        // Picker data for the create/restrict modals: boundaries the owner may anchor
        // grants at, each with its grantable operations. Refreshed per modal open so a
        // role change between page load and create is reflected without a reload.
        [HttpGet]
        public IActionResult GrantOptions()
        {
            if (!_config.Enabled || !_tokens.IsGenerationStateHealthy)
                return Json(Array.Empty<ApiTokenBoundaryOptions>());

            return Json(_grantOptions.GetBoundaryOptions(StoredUser ?? CurrentUser));
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

            var name = (request.Name ?? string.Empty).Trim();
            if (name.Length is < 1 or > MaxNameLength)
                return Fail("invalid_name", $"The token name must be 1-{MaxNameLength} characters long.");

            var description = (request.Description ?? string.Empty).Trim();
            if (description.Length > MaxDescriptionLength)
                return Fail("invalid_description", $"The description must be at most {MaxDescriptionLength} characters long.");

            if (request.Grants is null || request.Grants.Count == 0)
                return Fail("no_grants", "Select at least one operation for the token.");

            if (!TryBuildGrants(request.Grants, out var grants, out var grantError))
                return grantError;

            var ownerId = CurrentUser.Id;

            foreach (var grant in grants)
            {
                if (!_grantOptions.IsGrantableByOwner(StoredUser ?? CurrentUser, grant.Operation,
                        (ApiTokenBoundaryKind)grant.BoundaryKind, grant.BoundaryId))
                    return Fail("grant_not_allowed",
                        $"The grant '{grant.Operation}' is not available to you at this boundary.");
            }

            DateTime? expiresAtUtc;
            if (request.ExpiresAtUtc is null)
            {
                if (!_config.AllowNoExpiration)
                    return Fail("no_expiration_not_allowed",
                        "Tokens without expiration are disabled by the server configuration.");

                expiresAtUtc = null;
            }
            else
            {
                var requested = NormalizeUtc(request.ExpiresAtUtc.Value);

                if (requested <= DateTime.UtcNow)
                    return Fail("past_expiry", "The expiration date must be in the future.");

                expiresAtUtc = requested;
            }

            // Friendly pre-check only: the hard quota bound is enforced by the manager
            // (its ApiTokens.MaxTokensPerUser) inside the same serialized create path,
            // so a concurrent request cannot slip past the cap even when both pass here.
            if (_tokens.CountQuotaEligibleTokens(ownerId) >= _config.MaxTokensPerUser)
                return Fail("quota",
                    $"The token limit is reached ({_config.MaxTokensPerUser}). Revoke an unused token to free a slot.");

            if (!_tokens.TryCreateToken(ownerId, name, description, grants, expiresAtUtc,
                    CurrentUser.Name, out var created, out var fullToken))
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
        public IActionResult RestrictToken([FromBody] RestrictTokenRequest request)
        {
            if (!_config.Enabled)
                return Disabled();
            if (!_tokens.IsGenerationStateHealthy)
                return Unhealthy();

            if (request is null)
                return Fail("invalid_request", "The request body is missing.");

            if (!TryResolveOwnLiveToken(request.EntityId, out var failure))
                return failure;

            if (request.Grants is null)
                return Fail("no_grants", "The remaining grant set is required (an empty list strips every grant).");

            if (!TryBuildGrants(request.Grants, out var grants, out var grantError))
                return grantError;

            DateTime? shortenedExpiryUtc = null;

            if (request.ExpiresAtUtc is not null)
            {
                var requested = NormalizeUtc(request.ExpiresAtUtc.Value);

                if (requested <= DateTime.UtcNow)
                    return Fail("past_expiry", "The expiration date must be in the future.");

                shortenedExpiryUtc = requested;
            }

            if (!_tokens.TryRestrictToken(request.EntityId, grants, shortenedExpiryUtc,
                    CurrentUser.Name, out _))
                return Fail("restrict_failed",
                    "Restriction failed: grants may only be removed and the expiration only shortened.");

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

            if (!TryResolveOwnLiveToken(request.EntityId, out var failure))
                return failure;

            DateTime? shortenedExpiryUtc = null;

            if (request.ExpiresAtUtc is not null)
            {
                var requested = NormalizeUtc(request.ExpiresAtUtc.Value);

                if (requested <= DateTime.UtcNow)
                    return Fail("past_expiry", "The expiration date must be in the future.");

                shortenedExpiryUtc = requested;
            }

            // Rotation deliberately re-mints the token's ALREADY-ISSUED grant set without
            // re-running IsGrantableByOwner: the owner filter gates issuance (create),
            // grants never expand through rotation, and effective rights are intersected
            // with the owner's CURRENT roles on every request anyway — re-checking here
            // would only block rotation (never the token itself) after a role loss.
            if (!_tokens.TryRotateToken(request.EntityId, shortenedExpiryUtc, CurrentUser.Name,
                    out _, out var fullToken))
                return Fail("rotate_failed",
                    "Rotation failed: the new expiration must not be later than the current one.");

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


        // Normalizes the requested pairs into grant entities: canonical boundary ids,
        // catalog-checked operations, and no duplicate operation+boundary pairs (the
        // manager rejects duplicates fail-closed; the surface error is friendlier).
        private bool TryBuildGrants(List<ProfileGrantRequest> request, out List<ApiTokenGrantEntity> grants,
            out IActionResult error)
        {
            grants = null;
            error = null;

            var seen = new HashSet<(string, byte, string)>();
            var result = new List<ApiTokenGrantEntity>(request.Count);

            foreach (var grant in request)
            {
                if (grant is null || string.IsNullOrWhiteSpace(grant.Operation) ||
                    !Enum.TryParse(grant.BoundaryKind, ignoreCase: true, out ApiTokenBoundaryKind kind))
                {
                    error = Fail("invalid_grant", "Every grant needs a known operation and boundary kind.");
                    return false;
                }

                string boundaryId;

                switch (kind)
                {
                    case ApiTokenBoundaryKind.Global:
                        boundaryId = string.Empty;
                        break;

                    case ApiTokenBoundaryKind.Product:
                    case ApiTokenBoundaryKind.Folder:
                        if (!Guid.TryParse(grant.BoundaryId, out var id))
                        {
                            error = Fail("invalid_grant", "Every grant needs a valid boundary id.");
                            return false;
                        }

                        boundaryId = id.ToString();
                        break;

                    default:
                        error = Fail("invalid_grant", "Every grant needs a known operation and boundary kind.");
                        return false;
                }

                if (!seen.Add((grant.Operation.Trim(), (byte)kind, boundaryId)))
                {
                    error = Fail("duplicate_grant", "Duplicate operation and boundary pairs are not allowed.");
                    return false;
                }

                result.Add(new ApiTokenGrantEntity
                {
                    Operation = grant.Operation.Trim(),
                    BoundaryKind = (byte)kind,
                    BoundaryId = boundaryId,
                });
            }

            grants = result;
            return true;
        }


        // Resolves the entity id to the caller's own live token; a foreign, unknown,
        // revoked or expired id all produce the same indistinguishable failure.
        private bool TryResolveOwnLiveToken(Guid entityId, out IActionResult failure)
        {
            var token = _tokens.GetTokenByEntityId(entityId);

            var isOwnLive = token is not null &&
                            token.OwnerUserId == CurrentUser.Id &&
                            token.RevokedAtUtc is null &&
                            (token.ExpiresAtUtc is null ||
                             new DateTime(token.ExpiresAtUtc.Value, DateTimeKind.Utc) > DateTime.UtcNow);

            if (isOwnLive)
            {
                failure = null;
                return true;
            }

            failure = Fail("not_found", "Token not found.");
            return false;
        }


        private List<ProfileTokenViewModel> BuildTokenList(Guid ownerId)
        {
            var now = DateTime.UtcNow;
            var globalGeneration = _tokens.GlobalRevocationGeneration;
            var ownerGeneration = _tokens.GetOwnerRevocationGeneration(ownerId);

            return _tokens.GetTokensByOwner(ownerId)
                .OrderByDescending(t => t.CreatedAtUtc)
                .Select(t => new ProfileTokenViewModel
                {
                    EntityId = t.EntityId,
                    Name = t.Name,
                    Description = t.Description,
                    Grants = t.Grants.Select(DescribeGrant).ToList(),
                    Status = DescribeStatus(t, now, globalGeneration, ownerGeneration),
                    CreatedAtUnixMs = ToUnixMs(t.CreatedAtUtc),
                    ExpiresAtUnixMs = t.ExpiresAtUtc is null ? null : ToUnixMs(t.ExpiresAtUtc.Value),
                    LastUsedAtUnixMs = t.LastUsedAtUtc is null ? null : ToUnixMs(t.LastUsedAtUtc.Value),
                })
                .ToList();
        }


        // Generation-invalidated records (an emergency revoke advanced a generation past
        // the token's at-issue stamps) have no per-row timestamp — without this check
        // they would list as "active" with live lifecycle buttons while no longer
        // authenticating, and would disagree with the quota counter that already
        // excludes them.
        private static string DescribeStatus(ApiTokenInfo token, DateTime now,
            long globalGeneration, long ownerGeneration)
        {
            if (token.RevokedAtUtc is not null)
                return "revoked";

            if (token.GlobalRevocationGenerationAtIssue < globalGeneration ||
                token.OwnerRevocationGenerationAtIssue < ownerGeneration)
                return "invalidated";

            return token.ExpiresAtUtc is not null &&
                   new DateTime(token.ExpiresAtUtc.Value, DateTimeKind.Utc) <= now ? "expired" : "active";
        }


        private ProfileTokenGrantViewModel DescribeGrant(ApiTokenGrantEntity grant)
        {
            var kind = (ApiTokenBoundaryKind)grant.BoundaryKind;
            string name = "removed boundary";

            if (kind == ApiTokenBoundaryKind.Global)
            {
                name = "Global";
            }
            else if (Guid.TryParse(grant.BoundaryId, out var id))
            {
                if (kind == ApiTokenBoundaryKind.Product && _cache.TryGetProductNameById(id, out var productName))
                    name = productName;
                else if (kind == ApiTokenBoundaryKind.Folder &&
                         _folders.TryGetValue(id, out var folder) && folder is not null)
                    name = folder.Name;
            }

            return new ProfileTokenGrantViewModel(grant.Operation,
                kind.ToString().ToLowerInvariant(), grant.BoundaryId, name);
        }


        private List<ProfileProductRoleViewModel> BuildProductBadges(User user) =>
            user.ProductsRoles
                .GroupBy(r => r.Item1)
                .Select(g => (Id: g.Key, IsManager: g.Any(r => r.Item2 == ProductRoleEnum.ProductManager)))
                .Select(r => new ProfileProductRoleViewModel(
                    _cache.TryGetProductNameById(r.Id, out var name) ? name : r.Id.ToString(), r.IsManager))
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();


        private static long ToUnixMs(long ticks)
        {
            // Entity timestamps are .NET ticks (since 0001-01-01); the page script wants
            // Unix milliseconds — subtract the epoch offset before scaling.
            return (ticks - DateTime.UnixEpoch.Ticks) / TimeSpan.TicksPerMillisecond;
        }


        // The manager's UTC contract treats Kind.Unspecified as UTC; ToUniversalTime()
        // would instead interpret it as the SERVER's local zone and shift the stored
        // expiry on every non-UTC host. Normalize exactly the way the contract states.
        private static DateTime NormalizeUtc(DateTime value) =>
            value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
                _ => value.ToUniversalTime(),
            };


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
