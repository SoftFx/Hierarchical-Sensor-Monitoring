using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using HSMServer.Authentication;
using HSMServer.BackgroundServices;
using Microsoft.AspNetCore.Http;

namespace HSMServer.Middleware
{
    // The measurement half of the API-token usage monitoring (#1402): every
    // request to /api/v1 or /mcp is timed and attributed to the API token that
    // authenticated it — the operator's performance picture of management-API
    // access ("which tokens are used, how much, how slow"). Metric sending is
    // best-effort by construction: nothing in this middleware may influence
    // the measured request except (negligible) timing cost.
    //
    // Position: AFTER UseAuthentication (the token principal exists) but
    // BEFORE UseAuthorization — a rejected request never reaches middleware
    // registered after it, and the 401s ARE the auth-failure signal. The
    // measured duration therefore includes authorization, which is honest:
    // it is all server-side handling the caller waits for.
    public sealed class ApiTokenUsageMiddleware(RequestDelegate next, IApiTokenUsageMonitor monitor,
        IApiTokenManager tokens, IUserManager users)
    {
        private static readonly double TicksToMilliseconds = 1000.0 / Stopwatch.Frequency;


        public async Task InvokeAsync(HttpContext context)
        {
            if (!IsMeasuredPath(context.Request.Path))
            {
                await next(context);
                return;
            }

            var isMcp = context.Request.Path.StartsWithSegments("/mcp", StringComparison.OrdinalIgnoreCase);
            var tokenIdentity = FindTokenIdentity(context);

            // Resolved lazily AFTER the handler ran: the lookup is only worth
            // its cost when there is a duration to attribute.
            var startedAt = Stopwatch.GetTimestamp();

            try
            {
                await next(context);
            }
            finally
            {
                // Never let monitoring observations escape into the request's
                // error handling — an exception here would replace the real
                // outcome of a finished request.
                try
                {
                    Observe(context, tokenIdentity, isMcp, Stopwatch.GetTimestamp() - startedAt);
                }
                catch
                {
                    // Swallowed deliberately: metrics must not break traffic.
                }
            }
        }


        private void Observe(HttpContext context, System.Security.Claims.ClaimsIdentity tokenIdentity,
            bool isMcp, long durationTicks)
        {
            var durationMs = durationTicks * TicksToMilliseconds;

            if (tokenIdentity is not null && TryResolve(tokenIdentity, out var login, out var entityId))
            {
                if (isMcp)
                    monitor.AddMcpRequest(login, entityId, durationMs);
                else
                    monitor.AddRestRequest(login, entityId, durationMs);

                return;
            }

            // No token identity and the challenge answered 401: the credential
            // was missing or invalid — nothing to attribute, one aggregate tick.
            // (A cookie-principal request — the /api/v1/api-tokens family — is
            // not token usage and is not a token auth failure; it lands here
            // only when its response is 401, same as any unauthenticated path.)
            if (tokenIdentity is null && context.Response.StatusCode == StatusCodes.Status401Unauthorized)
                monitor.AddAuthenticationFailure();
        }


        // The principal stays minimal by design (#1402 grilling): login and
        // EntityId are resolved per request from the authoritative stores —
        // an O(1) lookup that also stays current across user renames.
        private bool TryResolve(System.Security.Claims.ClaimsIdentity identity, out string login, out string entityId)
        {
            login = null;
            entityId = null;

            var tokenIdClaim = FindClaim(identity, HsmApiTokenClaims.TokenId);
            if (tokenIdClaim is null)
                return false;

            var token = tokens.GetToken(tokenIdClaim);

            if (token is null)
                return false; // revoked mid-request: authenticated, but nothing to attribute anymore

            var owner = users[token.OwnerUserId];

            if (owner is null)
                return false; // deleted owner: same race, skip rather than mis-attribute

            login = SanitizeLogin(owner.Name);
            entityId = token.EntityId.ToString("D");

            return true;
        }


        private static System.Security.Claims.ClaimsIdentity FindTokenIdentity(HttpContext context) =>
            // The management policy admits exactly one HsmApiToken identity;
            // a cookie-only principal (the token lifecycle family) has none.
            context.User.Identities.FirstOrDefault(identity =>
                identity.AuthenticationType == HsmApiTokenDefaults.AuthenticationScheme);

        private static string FindClaim(System.Security.Claims.ClaimsIdentity identity, string claimType) =>
            identity.Claims.FirstOrDefault(claim => claim.Type == claimType)?.Value;

        // A login is free-form text and becomes a PATH SEGMENT: anything that
        // would split or corrupt the segment is collapsed to '_' — the tree
        // must stay one level per intended level no matter what the login
        // contains (#1402 acceptance).
        internal static string SanitizeLogin(string login) =>
            string.Join('_', login.Split('/', '\\')).Trim();

        private static bool IsMeasuredPath(PathString path) =>
            path.StartsWithSegments("/api/v1", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWithSegments("/mcp", StringComparison.OrdinalIgnoreCase);
    }
}
