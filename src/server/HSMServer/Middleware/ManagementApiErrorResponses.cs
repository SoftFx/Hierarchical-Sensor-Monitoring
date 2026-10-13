using System.Collections.Generic;
using System.Threading.Tasks;
using HSMServer.Model.ManagementApi;
using Microsoft.AspNetCore.Http;

namespace HSMServer.Middleware
{
    // The pipeline-side writer of the uniform error contract (see ManagementApiErrorDto):
    // the single serializer every non-MVC error path of the management API goes through —
    // the area guard's 404s, the legacy bearer guard's 401, the HsmApiToken challenge and
    // forbidden twin, and the /api exception handler — so the wire shape cannot drift
    // between enforcement points. WriteAsJsonAsync produces the same web-default camelCase
    // casing MVC uses for the controller-side results. The per-kind helpers pre-assemble
    // the code↔status pairs, and the raw writer is PRIVATE (#1371): no caller can emit a
    // mismatched pair, the 404 body stays byte-identical everywhere (the area's
    // anti-enumeration rule).
    public static class ManagementApiErrorResponses
    {
        // Pre-assembled pairs only — adding a status means adding a helper here that
        // pins its code, not a call site free-styling statusCode + error strings.
        private static Task WriteAsync(HttpContext context, int statusCode, string error, string message,
            Dictionary<string, object> details = null)
        {
            context.Response.StatusCode = statusCode;

            return context.Response.WriteAsJsonAsync(new ManagementApiErrorDto
            {
                Error = error,
                Message = message,
                Details = details,
            });
        }

        // The generic 404 of the area — the same constants ManagementApiErrors.NotFound()
        // serves the controllers with.
        public static Task WriteNotFound(HttpContext context) =>
            WriteAsync(context, StatusCodes.Status404NotFound,
                ManagementApiErrors.NotFoundCode, ManagementApiErrors.NotFoundMessage);

        public static Task WriteUnauthorized(HttpContext context, string message) =>
            WriteAsync(context, StatusCodes.Status401Unauthorized,
                ManagementApiErrors.UnauthorizedCode, message);

        // The Forbid twin of the bearer scheme's 401 challenge (defense-in-depth, see
        // HsmApiTokenHandler.HandleForbiddenAsync) — never the cookie scheme's redirect.
        public static Task WriteForbidden(HttpContext context, string message) =>
            WriteAsync(context, StatusCodes.Status403Forbidden,
                ManagementApiErrors.ForbiddenCode, message);

        // 500 with the incident's trace id — the only field allowed to vary per incident.
        public static Task WriteInternalError(HttpContext context, string traceId) =>
            WriteAsync(context, StatusCodes.Status500InternalServerError,
                ManagementApiErrors.InternalErrorCode, "An unexpected error occurred.",
                new Dictionary<string, object> { ["traceId"] = traceId });
    }
}
