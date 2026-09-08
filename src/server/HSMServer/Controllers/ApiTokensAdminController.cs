using System;
using System.Linq;
using HSMServer.Attributes;
using HSMServer.Authentication;
using HSMServer.Core.Journal;
using HSMServer.Core.Model;
using HSMServer.Model.ApiTokensAdmin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace HSMServer.Controllers
{
    // IsAdmin emergency levers of the API-token channel (#1356 step 4, PR B): revoke
    // every token of one user (Users-page row action) and of the whole deployment
    // (Configuration, next to the ApiTokens.Enabled kill switch). Both advance a
    // durable revocation generation — the manager's persist-first operation that
    // invalidates the target set before this returns — and never depend on the token
    // index being healthy or the channel being enabled: cleanup during kill-switch and
    // after storage damage is exactly what they exist for.
    //
    // Cookie-only by construction (BaseController's bare [Authorize]) plus
    // [AuthorizeIsAdmin]; mutations are JSON-in/JSON-out AJAX actions with
    // [ValidateAntiForgeryToken], following the profile mutation conventions. A
    // storage failure of the generation advance must not escape to the global HTML
    // error page: it is caught here and answered as a retryable failure carrying
    // HttpContext.TraceIdentifier — the same key that locates the NLog record.
    [AuthorizeIsAdmin]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class ApiTokensAdminController : BaseController
    {
        // The literal phrase an admin types to confirm a deployment-wide revoke; shown
        // next to the input in the Configuration modal.
        public const string RevokeAllConfirmationPhrase = "revoke-all";

        // Surface bound for the reason, deliberately the manager's free-text backstop
        // (ApiTokenManager's MaxFreeTextLength): the audit record truncates there, so
        // the surface rejects longer input friendly instead of silently shortening it.
        public const int MaxReasonLength = 256;

        // Stable journal anchor for deployment-scope records: every revoke-all event of
        // this server groups under one id, time-ordered, the way a user-scoped record
        // anchors on the target user's id.
        public static readonly Guid DeploymentRevocationAnchorId = new("0F5C3B8E-2D47-4A91-8E6B-9C2D1F7A4E30");

        private readonly IApiTokenManager _tokens;
        private readonly IJournalService _journal;
        private readonly ILogger<ApiTokensAdminController> _logger;

        public ApiTokensAdminController(IUserManager userManager, IApiTokenManager tokens,
            IJournalService journal, ILogger<ApiTokensAdminController> logger) : base(userManager)
        {
            _tokens = tokens;
            _journal = journal;
            _logger = logger;
        }


        // Live-token count for the Users-page confirmation modal: the number the admin
        // confirms against. Advisory like every count here — a token created between
        // this call and the revoke is still invalidated by the revoke itself.
        [HttpGet]
        public IActionResult UserTokenSummary(Guid userId)
        {
            if (userId == Guid.Empty)
                return SummaryFail();

            var user = _userManager[userId];

            if (user is null)
                return SummaryFail();

            return new JsonResult(new UserTokenSummaryResponse
            {
                Ok = true,
                UserName = user.Name,
                LiveTokens = _tokens.CountQuotaEligibleTokens(userId),
            });
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult RevokeUserTokens([FromBody] RevokeUserTokensRequest request)
        {
            if (request is null)
                return Fail("invalid_request", "The request body is missing.");

            if (request.UserId == Guid.Empty)
                return Fail("not_found", "User not found.");

            // Resolved through the user manager first: an unknown or removed owner is
            // the not-found case (a removed user's tokens are already dead at the
            // authentication layer, which resolves the owner the same way).
            var user = _userManager[request.UserId];

            if (user is null)
                return Fail("not_found", "User not found.");

            // The typed confirmation names the target (initiative requirement) and
            // defeats a wrong-row misclick; case-insensitive on purpose — the
            // deliberation is in the typing, not the casing.
            if (!string.Equals((request.Confirmation ?? string.Empty).Trim(),
                    user.Name?.Trim(), StringComparison.OrdinalIgnoreCase))
                return Fail("invalid_confirmation",
                    $"Type the user's exact username ({user.Name}) to confirm the emergency revoke.");

            if (!TryNormalizeReason(request.Reason, out var reason))
                return Fail("invalid_reason", $"A non-empty reason of at most {MaxReasonLength} characters is required.");

            var affected = _tokens.CountQuotaEligibleTokens(request.UserId);
            var oldGeneration = _tokens.GetOwnerRevocationGeneration(request.UserId);

            long newGeneration;

            try
            {
                // The emergency lever itself: persist-first, and a throw means the
                // revoke did NOT happen (both states stay at the old generation).
                newGeneration = _tokens.AdvanceOwnerRevocationGeneration(request.UserId);
            }
            catch (Exception e)
            {
                return RevocationFailed(e, request.UserId, $"users/{user.Name} ({request.UserId})",
                    affected, oldGeneration, reason);
            }

            WriteAudit(anchorId: request.UserId, scope: $"users/{user.Name} ({request.UserId})",
                oldGeneration, newGeneration, affected, reason, correlationId: null);

            _logger.LogInformation(
                "Emergency owner revocation by {Admin}: user {UserId}, generation {OldGeneration} -> {NewGeneration}, {AffectedTokens} token(s) affected",
                CurrentUser.Name, request.UserId, oldGeneration, newGeneration, affected);

            return new JsonResult(new EmergencyRevokeResponse
            {
                Ok = true,
                NewGeneration = newGeneration,
                AffectedTokens = affected,
            });
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult RevokeAllTokens([FromBody] RevokeAllTokensRequest request)
        {
            if (request is null)
                return Fail("invalid_request", "The request body is missing.");

            // The phrase is typed, not checked: naming the whole deployment is the
            // initiative's confirmation requirement for the widest possible revoke.
            if (!string.Equals((request.Confirmation ?? string.Empty).Trim(),
                    RevokeAllConfirmationPhrase, StringComparison.Ordinal))
                return Fail("invalid_confirmation",
                    $"Type the phrase '{RevokeAllConfirmationPhrase}' exactly to confirm the deployment-wide revoke.");

            if (!TryNormalizeReason(request.Reason, out var reason))
                return Fail("invalid_reason", $"A non-empty reason of at most {MaxReasonLength} characters is required.");

            var affected = _tokens.CountQuotaEligibleTokensGlobally();
            var oldGeneration = _tokens.GlobalRevocationGeneration;

            long newGeneration;

            try
            {
                newGeneration = _tokens.AdvanceGlobalRevocationGeneration();
            }
            catch (Exception e)
            {
                return RevocationFailed(e, DeploymentRevocationAnchorId, "deployment (all users)",
                    affected, oldGeneration, reason);
            }

            WriteAudit(anchorId: DeploymentRevocationAnchorId, scope: "deployment (all users)",
                oldGeneration, newGeneration, affected, reason, correlationId: null);

            _logger.LogInformation(
                "Emergency global revocation by {Admin}: generation {OldGeneration} -> {NewGeneration}, {AffectedTokens} token(s) affected",
                CurrentUser.Name, oldGeneration, newGeneration, affected);

            return new JsonResult(new EmergencyRevokeResponse
            {
                Ok = true,
                NewGeneration = newGeneration,
                AffectedTokens = affected,
            });
        }


        // The generation advance threw: nothing was revoked, nothing was published, and
        // the caller may retry. The exception's text never crosses the wire — the
        // correlation id is the key that locates the NLog record written here.
        private IActionResult RevocationFailed(Exception exception, Guid anchorId, string scope, int affected,
            long oldGeneration, string reason)
        {
            var correlationId = HttpContext.TraceIdentifier;

            _logger.LogError(exception,
                "Emergency revocation failed (correlation {CorrelationId}); the revocation generation was NOT advanced and no tokens were invalidated",
                correlationId);

            WriteAudit(anchorId: anchorId, scope: scope, oldGeneration, newGeneration: null,
                affected, reason, correlationId);

            return new JsonResult(new EmergencyRevokeResponse
            {
                Ok = false,
                Error = "revoke_failed",
                Message = "The revoke did not complete; nothing was changed. You can retry. If it repeats, quote the correlation id in the server log.",
                CorrelationId = correlationId,
            });
        }

        // Audit-of-record for the emergency surface, through the same durable journal
        // the tree changes use. Best-effort by contract: the revoke has already
        // happened (or provably failed) when this runs, so a journaling failure is
        // logged with the correlation id and must neither fail the response nor
        // pretend the revoke did not take effect.
        private void WriteAudit(Guid anchorId, string scope, long oldGeneration, long? newGeneration,
            int affected, string reason, string correlationId)
        {
            try
            {
                _journal.AddRecord(new JournalRecordModel(anchorId, CurrentInitiator)
                {
                    Enviroment = "API tokens",
                    PropertyName = newGeneration.HasValue ? "Emergency revoke" : "Emergency revoke (failed)",
                    OldValue = $"generation {oldGeneration}; {affected} live token(s) in scope; reason: {reason}",
                    NewValue = newGeneration.HasValue
                        ? $"generation {newGeneration.Value}"
                        : $"no change; correlation id: {correlationId}",
                    Path = scope,
                });
            }
            catch (Exception e)
            {
                _logger.LogError(e,
                    "Emergency revoke audit record could not be persisted (anchor {AnchorId}, correlation {CorrelationId}); the revocation itself is unaffected",
                    anchorId, correlationId ?? "none");
            }
        }

        // The reason is required free text: control characters become spaces (the
        // journal record renders this value), the result is trimmed, and input that is
        // empty or over the surface bound is rejected — the admin typed something the
        // audit will not carry, so they should rephrase rather than be silently
        // truncated.
        private static bool TryNormalizeReason(string reason, out string normalized)
        {
            normalized = new string((reason ?? string.Empty)
                    .Select(c => char.IsControl(c) ? ' ' : c)
                    .ToArray())
                .Trim();

            return normalized.Length is >= 1 and <= MaxReasonLength;
        }

        private static IActionResult Fail(string error, string message) =>
            new JsonResult(new EmergencyRevokeResponse { Ok = false, Error = error, Message = message });

        // The summary endpoint's own failure shape: the same answer type as its success,
        // just with Ok false — the modal script branches on ok alone.
        private static IActionResult SummaryFail() =>
            new JsonResult(new UserTokenSummaryResponse { Ok = false });
    }
}
