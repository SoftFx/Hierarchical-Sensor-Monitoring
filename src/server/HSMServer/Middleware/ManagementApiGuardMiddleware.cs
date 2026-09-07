using System.Linq;
using System.Threading.Tasks;
using HSMServer.Authentication;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace HSMServer.Middleware
{
    // Fail-closed area convention for /api/v1 (initiative step 3). An endpoint inside the
    // management area is reachable only when ALL of the following hold:
    //   - the request arrived on the SitePort listener (management is never served on
    //     SensorPort, whatever the routing table shares between the listeners);
    //   - a route matched (there is an endpoint);
    //   - the endpoint carries [ManagementApi];
    //   - the endpoint requires its family's authorization — the HsmApiToken management
    //     policy outside the reserved cookie-only /api/v1/api-tokens family, a cookie
    //     [Authorize] (the default policy) inside it — so it is never anonymous (there
    //     is no fallback policy behind the guard).
    // Anything else under /api/v1 is a 404 BEFORE controller execution — including a
    // route someone adds without the metadata, which is exactly the default this guard
    // exists to enforce. 404 rather than 403 so SensorPort responses do not confirm that a
    // management route exists. Since #1353 the 404 carries the area's uniform JSON error
    // body — the generic one the controllers also use, so a guard rejection is
    // indistinguishable from an unknown id and confirms nothing about routing either.
    public sealed class ManagementApiGuardMiddleware(RequestDelegate next, HsmListenerBindings listeners)
    {
        // The sole v1 route family that authorizes through the cookie scheme (token
        // lifecycle, step 4). Keep in sync with the step-4 controller's route root.
        private const string CookieOnlyRouteRoot = "/api/v1/api-tokens";


        public Task InvokeAsync(HttpContext context)
        {
            if (!LegacyBearerGuardMiddleware.IsManagementAreaPath(context.Request.Path))
                return next(context);

            // Listener allow-list first: the management area is SitePort-only, so even a
            // perfectly marked endpoint is unavailable on the other listener.
            if (!listeners.IsSitePort(context.Connection.LocalPort))
                return NotFound(context);

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

        private static bool IsReservedCookieOnlyFamily(PathString path) =>
            path.StartsWithSegments(CookieOnlyRouteRoot, System.StringComparison.OrdinalIgnoreCase);

        private static bool RequiresManagementPolicy(EndpointMetadataCollection metadata) =>
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
