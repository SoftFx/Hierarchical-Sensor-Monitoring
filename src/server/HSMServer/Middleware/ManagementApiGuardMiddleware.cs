using System.Linq;
using System.Threading.Tasks;
using HSMServer.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace HSMServer.Middleware
{
    // Fail-closed area convention for /api/v1 (initiative step 3). An endpoint inside the
    // management area is reachable only when ALL of the following hold:
    //   - a route matched (there is an endpoint);
    //   - the endpoint carries [ManagementApi];
    //   - the endpoint requires its family's authorization — the HsmApiToken management
    //     policy outside the reserved cookie-only /api/v1/api-tokens family, a cookie
    //     [Authorize] (the default policy) inside it — so it is never anonymous (there
    //     is no fallback policy behind the guard).
    // Anything else under /api/v1 is a 404 BEFORE controller execution — including a
    // route someone adds without the metadata, which is exactly the default this guard
    // exists to enforce. (Until #1590 the area was also listener-gated SitePort-only;
    // with the single listener the auth rules alone carry the isolation, and the 404
    // keeps its "does not confirm that a management route exists" property.) Since
    // #1353 the 404 carries the area's uniform JSON error body — the generic one the
    // controllers also use, so a guard rejection is indistinguishable from an unknown
    // id and confirms nothing about routing either.
    public sealed class ManagementApiGuardMiddleware(RequestDelegate next)
    {
        // The sole v1 route family that authorizes through the cookie scheme (token
        // lifecycle, step 4). Keep in sync with the step-4 controller's route root.
        // Internal: sibling middleware (token-usage measurement) excludes the
        // family by the same constant — cookie traffic is not token traffic.
        internal const string CookieOnlyRouteRoot = "/api/v1/api-tokens";


        public Task InvokeAsync(HttpContext context)
        {
            if (!LegacyBearerGuardMiddleware.IsManagementAreaPath(context.Request.Path))
                return next(context);

            var endpoint = context.GetEndpoint();

            if (endpoint is null)
                return NotFound(context);

            var metadata = endpoint.Metadata;

            if (metadata.GetMetadata<ManagementApiAttribute>() is null)
                return NotFound(context);

            if (metadata.GetMetadata<IAllowAnonymous>() is not null)
                return NotFound(context);

            // The endpoint must require its family's authorization. Absence of
            // [Authorize] is not [AllowAnonymous] — with no fallback policy it would be
            // anonymous — so a bare [ManagementApi] endpoint is unreachable, in the
            // reserved family exactly as everywhere else in the area.
            var requiresAuthorization = IsReservedCookieOnlyFamily(context.Request.Path)
                ? RequiresDefaultPolicy(metadata)
                : RequiresManagementPolicy(metadata);

            if (!requiresAuthorization)
                return NotFound(context);

            return next(context);
        }

        internal static bool IsReservedCookieOnlyFamily(PathString path) =>
            path.StartsWithSegments(CookieOnlyRouteRoot, System.StringComparison.OrdinalIgnoreCase);

        // Internal for McpEndpointGuardMiddleware (#1392 review): the /mcp
        // surface enforces the same policy rule without the area marker.
        internal static bool RequiresManagementPolicy(EndpointMetadataCollection metadata) =>
            metadata.OfType<AuthorizeAttribute>().Any(a => a.Policy == HsmApiTokenDefaults.ManagementPolicy);

        // The reserved family authorizes through the cookie-pinned DefaultPolicy: a bare
        // [Authorize] with no named policy and no explicit schemes — a scheme-bearing
        // attribute would union the HsmApiToken scheme into the cookie policy and let a
        // token principal into the family this guard keeps cookie-only.
        private static bool RequiresDefaultPolicy(EndpointMetadataCollection metadata) =>
            metadata.OfType<AuthorizeAttribute>().Any(a =>
                string.IsNullOrEmpty(a.Policy) && string.IsNullOrEmpty(a.AuthenticationSchemes));

        private static Task NotFound(HttpContext context) =>
            ManagementApiErrorResponses.WriteNotFound(context);
    }
}
