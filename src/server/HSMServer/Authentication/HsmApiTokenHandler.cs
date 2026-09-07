using System;
using System.Security.Claims;
using System.Threading.Tasks;
using HSMServer.Model.Authentication;
using HSMServer.Model.ManagementApi;
using HSMServer.Middleware;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using System.Text.Encodings.Web;

namespace HSMServer.Authentication
{
    // Authentication handler for the management-API bearer credential (initiative
    // docs/initiatives/fine-grained-api-token-authentication.md, step 3). Authentication
    // ONLY: it reads the Authorization header, delegates the single fail-closed decision to
    // IApiTokenManager.TryAuthenticate, verifies the owner still exists, and builds a
    // minimal single-identity principal. Permission intersection, resource authorization
    // and auditing live in their own components, never here.
    //
    // Every rejection is one indistinguishable generic failure: no status distinguishes an
    // unknown token from a revoked one, and the challenge is a plain 401 bearer challenge
    // that never redirects like the cookie scheme.
    public sealed class HsmApiTokenHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private readonly IApiTokenManager _tokens;
        private readonly IUserManager _users;
        private readonly IApiTokenSecurityEventSink _securityEvents;
        private readonly ApiTokenInvalidAttemptLimiter _invalidAttempts;


        public HsmApiTokenHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
            UrlEncoder encoder, IApiTokenManager tokens, IUserManager users, IApiTokenSecurityEventSink securityEvents,
            ApiTokenInvalidAttemptLimiter invalidAttempts)
            : base(options, logger, encoder)
        {
            _tokens = tokens;
            _users = users;
            _securityEvents = securityEvents;
            _invalidAttempts = invalidAttempts;
        }


        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            // Read only the Authorization header; tokens in URLs, query, cookies or bodies
            // are never credentials for this scheme.
            if (!Request.Headers.TryGetValue(HeaderNames.Authorization, out var headerValues))
                return Task.FromResult(AuthenticateResult.NoResult());

            // Duplicated Authorization values are not a parseable credential: joined by
            // ", " they would masquerade as another scheme, so the bearer inside would be
            // invisible. Treat the ambiguous shape as no credential — no lookup happens.
            if (headerValues.Count != 1)
                return Task.FromResult(AuthenticateResult.NoResult());

            var header = headerValues.ToString();
            if (string.IsNullOrEmpty(header))
                return Task.FromResult(AuthenticateResult.NoResult());

            // Another authentication scheme is clearly selected - not this handler's
            // credential. No token lookup happens for anything without the HSM prefix.
            if (!ApiTokenMaterial.TryReadBearerCredential(header, out var credential) ||
                !credential.StartsWith(ApiTokenMaterial.TokenPrefix, StringComparison.Ordinal))
                return Task.FromResult(AuthenticateResult.NoResult());

            // A credential that claims the HSM prefix but does not survive strict parsing
            // is predictably rejected before any index lookup.
            if (!ApiTokenMaterial.IsValidCredentialShape(credential))
            {
                RecordFailure(tokenId: null, ownerId: null);
                return Task.FromResult(AuthenticateResult.Fail("Invalid bearer credential."));
            }

            var tokenId = ApiTokenMaterial.TokenIdOf(credential);

            // The single authentication decision: strict parse, index lookup,
            // stored-or-dummy constant-time verifier compare, revocation/expiry/
            // generation stamps and boot health - assembled by the manager so this
            // handler cannot omit a check.
            if (!_tokens.TryAuthenticate(credential, out var token))
            {
                RecordFailure(tokenId, ownerId: null);
                return Task.FromResult(AuthenticateResult.Fail("Invalid bearer credential."));
            }

            // Owner deletion invalidates the token on the next request.
            if (_users[token.OwnerUserId] is null)
            {
                RecordFailure(tokenId, token.OwnerUserId);
                return Task.FromResult(AuthenticateResult.Fail("Invalid bearer credential."));
            }

            var identity = new ClaimsIdentity(authenticationType: Scheme.Name,
                claims:
                [
                    new Claim(HsmApiTokenClaims.OwnerUserId, token.OwnerUserId.ToString()),
                    new Claim(HsmApiTokenClaims.TokenId, tokenId),
                ]);

            _tokens.MarkUsed(tokenId);

            // Sampled inside the sink: successes are volume-controlled, never blocking.
            _securityEvents.Record(new ApiTokenSecurityEvent(
                ApiTokenSecurityEventKind.AuthSucceeded, tokenId, token.OwnerUserId,
                CorrelationId: Context.TraceIdentifier,
                Source: DescribeSource()));

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(identity), Scheme.Name)));
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            // A generic, non-redirecting bearer challenge with the area's uniform JSON
            // body (#1353) — a machine client reads the 401 without parsing HTML. The
            // cookie scheme's login redirect must never fire for management endpoints.
            Response.Headers.WWWAuthenticate = "Bearer";

            return ManagementApiErrorResponses.WriteUnauthorized(Context, "A valid bearer token is required.");
        }

        // Defense-in-depth for the contract: the management policy carries requirements
        // beyond RequireAuthenticatedUser (HsmApiTokenOnlyRequirement), so Forbid — not
        // Challenge — answers when an authenticated principal fails them. The base
        // implementation would return a bare 403 with an empty body, the one shape the
        // error contract promises never happens.
        protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
            ManagementApiErrorResponses.WriteAsync(Context, StatusCodes.Status403Forbidden,
                ManagementApiErrors.ForbiddenCode, "The token is not permitted to use the management API.");


        private void RecordFailure(string tokenId, Guid? ownerId)
        {
            // Rate-limited per source (initiative: failures are "rate-limited/coalesced"):
            // beyond the budget the event is dropped (counted and logged by the limiter)
            // — the authentication decision above is unchanged either way.
            var source = DescribeSource();

            if (!_invalidAttempts.TryAcquire(DescribeSourceBucket()))
                return;

            _securityEvents.Record(new ApiTokenSecurityEvent(
                ApiTokenSecurityEventKind.AuthFailed,
                // Persist an id only when it really is one. The cheap shape check does not
                // validate the alphabet, and the security-event store must never receive
                // attacker-chosen bytes as a TokenId (the entity's safe-identifier
                // invariant) — an unauthenticated caller must not get a write channel
                // into the audit trail.
                ApiTokenMaterial.IsValidTokenId(tokenId) ? tokenId : null,
                ownerId,
                CorrelationId: Context.TraceIdentifier,
                Source: source));
        }

        // Remote endpoint only, in the ip:port form HSM already treats as safe source
        // context; never headers (which are attacker-controlled free text).
        private string DescribeSource() =>
            Context.Connection.RemoteIpAddress is { } ip ? $"{ip}:{Context.Connection.RemotePort}" : null;

        // Budget identity for the invalid-attempt limiter: the remote IP only. The port
        // is the client's EPHEMERAL port — a fresh value per TCP connection — so bucketing
        // on ip:port would hand every connection a brand-new budget (the bound becomes
        // MaxTrackedSources x limit from one host) and let one host fill the per-window
        // source registry with connection-scoped buckets, suppressing other sources'
        // events for the rest of the minute. The full ip:port stays in the event payload.
        private string DescribeSourceBucket() =>
            Context.Connection.RemoteIpAddress?.ToString();
    }
}
