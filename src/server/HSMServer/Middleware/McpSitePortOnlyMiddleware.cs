using System;
using System.Threading.Tasks;
using HSMServer.Mcp;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Http;

namespace HSMServer.Middleware
{
    // The MCP endpoint enumerates the token-visible monitoring surface (#1391) —
    // the same map the management area guard conceals from the SensorPort
    // listener. This gate answers /mcp off the SitePort with the area's uniform
    // 404 and lets everything else through untouched. It runs BEFORE
    // authentication (with the other guards): an unauthenticated probe on the
    // SensorPort must get the 404, never a 401 that confirms the endpoint.
    public sealed class McpSitePortOnlyMiddleware(RequestDelegate next, HsmListenerBindings listeners)
    {
        public Task InvokeAsync(HttpContext context)
        {
            if (context.Request.Path.StartsWithSegments(HsmMcp.EndpointPath, StringComparison.OrdinalIgnoreCase) &&
                !listeners.IsSitePort(context.Connection.LocalPort))
                return ManagementApiErrorResponses.WriteNotFound(context);

            return next(context);
        }
    }
}
