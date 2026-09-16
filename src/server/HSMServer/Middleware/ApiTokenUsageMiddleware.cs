using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using HSMServer.Authentication;
using HSMServer.BackgroundServices;
using HSMServer.Mcp;
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
    // Position: between UseAuthentication and UseAuthorization — a rejected
    // request never reaches middleware registered after the authorization one,
    // and the 401s ARE the auth-failure signal. The token identity is resolved
    // AT OBSERVATION TIME (after next returned): HsmApiToken is deliberately
    // not the default scheme, so UseAuthentication runs only the cookie
    // default and the token principal materializes INSIDE UseAuthorization,
    // which authenticates the policy's schemes and replaces context.User —
    // capturing it before next() would always see null on the real pipeline
    // (#1402 review). The measured duration includes authorization, which is
    // honest: it is all server-side handling the caller waits for.
    public sealed class ApiTokenUsageMiddleware(RequestDelegate next, IApiTokenUsageMonitor monitor,
        IApiTokenManager tokens, IUserManager users)
    {
        // Sticky observation failures (a stopping collector, MaxSensors
        // exceeded, a disposed registry) must not become one Warn per
        // management request: the first occurrence logs at once, repeats at
        // most once per interval (#1403 review).
        private const long ObservationFailureLogIntervalMs = 60_000;

        private static readonly double TicksToMilliseconds = 1000.0 / Stopwatch.Frequency;

        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        // Seeded "an interval ago": on a host that booted less than the interval
        // before the first failure — exactly the startup window where collector
        // problems appear — zero-init would swallow that first trace (#1403 r2).
        private static long _lastObservationFailureLog = -ObservationFailureLogIntervalMs;


        public async Task InvokeAsync(HttpContext context)
        {
            if (!ClassifyPath(context.Request.Path, out var isMcp))
            {
                await next(context);
                return;
            }

            var startedAt = Stopwatch.GetTimestamp();

            try
            {
                await next(context);
            }
            finally
            {
                // Never let monitoring observations escape into the request's
                // error handling — an exception here would replace the real
                // outcome of a finished request. Logged, not just swallowed:
                // a dead collector must leave a trace (CLAUDE.md rule 8).
                try
                {
                    // Resolved lazily AFTER the handler ran: the lookup is
                    // only worth its cost when there is a duration to
                    // attribute — and the token identity itself only exists
                    // by then (see the position comment).
                    Observe(context, FindTokenIdentity(context), isMcp, Stopwatch.GetTimestamp() - startedAt);
                }
                catch (Exception ex)
                {
                    LogObservationFailure(ex);
                }
            }
        }


        private void Observe(HttpContext context, ClaimsIdentity tokenIdentity,
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
        private bool TryResolve(ClaimsIdentity identity, out string login, out string entityId)
        {
            login = null;
            entityId = null;

            var tokenIdClaim = identity.FindFirst(HsmApiTokenClaims.TokenId)?.Value;
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


        private static ClaimsIdentity FindTokenIdentity(HttpContext context) =>
            // The management policy admits exactly one HsmApiToken identity;
            // a cookie-only principal (the token lifecycle family) has none.
            context.User.Identities.FirstOrDefault(identity =>
                identity.AuthenticationType == HsmApiTokenDefaults.AuthenticationScheme);

        private static void LogObservationFailure(Exception ex)
        {
            var now = Environment.TickCount64;
            var last = Volatile.Read(ref _lastObservationFailureLog);

            if (now - last >= ObservationFailureLogIntervalMs &&
                Interlocked.CompareExchange(ref _lastObservationFailureLog, now, last) == last)
                Logger.Warn(ex, "API-token usage observation failed");
        }

        // A login is free-form text and becomes a PATH SEGMENT: anything that
        // would split or corrupt the segment is collapsed to '_' — the tree
        // must stay one level per intended level no matter what the login
        // contains (#1402 acceptance). A login collapsing to empty cannot
        // happen through AddUser's validation, but a path like
        // "API tokens//<id>" must never be built either.
        internal static string SanitizeLogin(string login)
        {
            // A missing name must not NRE here either: the contract ("never
            // an empty path segment") is total.
            if (string.IsNullOrWhiteSpace(login))
                return "_";

            var sanitized = string.Join('_', login.Split('/', '\\')).Trim();

            return sanitized.Length == 0 ? "_" : sanitized;
        }

        // One classification pass: measured-or-not and the channel decide
        // together, so the route roots are matched exactly once per request.
        // The constants are the ones the guards already use — if either root
        // ever moves, measurement moves with it instead of silently stopping.
        private static bool ClassifyPath(PathString path, out bool isMcp)
        {
            if (LegacyBearerGuardMiddleware.IsManagementAreaPath(path))
            {
                // The reserved cookie-only family is not token traffic, and its
                // 401s (an expired browser session) are not TOKEN credential
                // failures either — MyCookieAuthenticationEvents answers 401
                // for everything under /api/v1 (#1403 review, round 2).
                if (ManagementApiGuardMiddleware.IsReservedCookieOnlyFamily(path))
                {
                    isMcp = false;
                    return false;
                }

                isMcp = false;
                return true;
            }

            // The ENDPOINT, not the prefix — the same semantics as the bearer
            // guard's exemption (#1392): MapMcp maps exactly this path, and
            // anything else under /mcp is not token traffic.
            if (path.Equals((PathString)HsmMcp.EndpointPath, StringComparison.OrdinalIgnoreCase))
            {
                isMcp = true;
                return true;
            }

            isMcp = false;
            return false;
        }
    }
}
