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
    //   - the endpoint is not anonymous;
    //   - outside the reserved cookie-only /api/v1/api-tokens family, the endpoint
    //     explicitly requires the HsmApiToken management policy.
    // Anything else under /api/v1 is a plain 404 BEFORE controller execution — including a
    // route someone adds without the metadata, which is exactly the default this guard
    // exists to enforce. 404 rather than 403 so SensorPort responses do not confirm that a
    // management route exists.
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

            if (!IsReservedCookieOnlyFamily(context.Request.Path) &&
                !RequiresManagementPolicy(metadata))
                return NotFound(context);

            return next(context);
        }

        private static bool IsReservedCookieOnlyFamily(PathString path) =>
            path.StartsWithSegments(CookieOnlyRouteRoot, System.StringComparison.OrdinalIgnoreCase);

        private static bool RequiresManagementPolicy(EndpointMetadataCollection metadata) =>
            metadata.OfType<AuthorizeAttribute>().Any(a => a.Policy == HsmApiTokenDefaults.ManagementPolicy);

        private static Task NotFound(HttpContext context)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }
    }
}
