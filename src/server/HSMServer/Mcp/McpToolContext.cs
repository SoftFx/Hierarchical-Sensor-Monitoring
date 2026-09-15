using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol;

namespace HSMServer.Mcp
{
    // The ambient-principal accessor shared by the tool classes (#1391 review:
    // the property was duplicated verbatim). The Streamable HTTP transport
    // executes a stateless tools/call INLINE within its POST, so the ambient
    // HTTP context flows into the tool and carries the principal the endpoint's
    // RequireAuthorization policy already admitted.
    internal static class McpToolContext
    {
        // Behind RequireAuthorization the principal is always there; the throw
        // is a defensive backstop, not a reachable path.
        public static ClaimsPrincipal UserOf(IHttpContextAccessor http) =>
            http.HttpContext?.User
            ?? throw new McpException("No authenticated HTTP context is available for this tool call.");
    }
}
