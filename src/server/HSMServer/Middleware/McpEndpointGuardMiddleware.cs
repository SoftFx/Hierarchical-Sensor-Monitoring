using System;
using System.Threading.Tasks;
using HSMServer.Mcp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace HSMServer.Middleware
{
    // The MCP endpoint enumerates the token-visible monitoring surface (#1391) —
    // the same map the management area guard conceals from unmarked routes. This
    // gate enforces the area's fail-closed rule for /mcp, which has no
    // [ManagementApi] marker to gate on: a MATCHED endpoint under it must require
    // the management policy and never be anonymous — exactly the rule the area
    // guard applies under /api/v1, so a future app.MapGet("/mcp/health", ...) is
    // unreachable without the credential, like a route dropped under /api/v1
    // without the metadata. Unmatched paths carry no endpoint and keep the
    // framework's own 404 — a DELIBERATE divergence from
    // ManagementApiGuardMiddleware, which 404s unmatched area paths with the
    // uniform body: /mcp has exactly one route, so an unmatched /mcp/** path
    // confirms nothing about it, and the framework's bare 404 is
    // indistinguishable enough (#1392 review, finding 6). For the same reason
    // the bearer guard exempts the whole /mcp prefix: an hsm_pat_ credential on
    // an unmatched /mcp/** path falls through to the bare 404 too, never a
    // legacy route. It runs BEFORE authentication (with the other guards), so a
    // violating endpoint answers the uniform 404 and never a 401 that confirms
    // the endpoint. (Until #1590 the gate was also listener-gated SitePort-only;
    // the single listener retired that dimension.)
    public sealed class McpEndpointGuardMiddleware(RequestDelegate next)
    {
        public Task InvokeAsync(HttpContext context)
        {
            if (context.Request.Path.StartsWithSegments(HsmMcp.EndpointPath, StringComparison.OrdinalIgnoreCase))
            {
                if (context.GetEndpoint() is { } endpoint &&
                    (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null ||
                     !ManagementApiGuardMiddleware.RequiresManagementPolicy(endpoint.Metadata)))
                    return ManagementApiErrorResponses.WriteNotFound(context);
            }

            return next(context);
        }
    }
}
