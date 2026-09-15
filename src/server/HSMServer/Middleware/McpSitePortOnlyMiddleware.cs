using System;
using System.Threading.Tasks;
using HSMServer.Mcp;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace HSMServer.Middleware
{
    // The MCP endpoint enumerates the token-visible monitoring surface (#1391) —
    // the same map the management area guard conceals from the SensorPort
    // listener. This gate answers /mcp off the SitePort with the area's uniform
    // 404 and lets everything else through untouched. It runs BEFORE
    // authentication (with the other guards): an unauthenticated probe on the
    // SensorPort must get the 404, never a 401 that confirms the endpoint.
    //
    // Fail-closed over the endpoint family too (#1392 review): /mcp has no
    // [ManagementApi] marker to gate on, so a MATCHED endpoint under it must
    // require the management policy and never be anonymous — the area guard's
    // rule, so a future app.MapGet("/mcp/health", ...) is unreachable without
    // the credential, exactly like a route dropped under /api/v1 without the
    // metadata. Unmatched paths carry no endpoint and keep the framework's own
    // 404 — a DELIBERATE divergence from ManagementApiGuardMiddleware, which
    // 404s unmatched area paths with the uniform body: /mcp has exactly one
    // route, so an unmatched /mcp/** path confirms nothing about it, and the
    // framework's bare 404 is indistinguishable enough (#1392 review, finding 6).
    // For the same reason the bearer guard exempts the whole /mcp prefix: an
    // hsm_pat_ credential on an unmatched /mcp/** path falls through to the
    // bare 404 too, never a legacy route.
    public sealed class McpSitePortOnlyMiddleware(RequestDelegate next, HsmListenerBindings listeners)
    {
        public Task InvokeAsync(HttpContext context)
        {
            if (context.Request.Path.StartsWithSegments(HsmMcp.EndpointPath, StringComparison.OrdinalIgnoreCase))
            {
                if (!listeners.IsSitePort(context.Connection.LocalPort))
                    return ManagementApiErrorResponses.WriteNotFound(context);

                if (context.GetEndpoint() is { } endpoint &&
                    (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null ||
                     !ManagementApiGuardMiddleware.RequiresManagementPolicy(endpoint.Metadata)))
                    return ManagementApiErrorResponses.WriteNotFound(context);
            }

            return next(context);
        }
    }
}
