using System.Collections.Generic;
using System.Threading.Tasks;
using HSMServer.Model.ManagementApi;
using Microsoft.AspNetCore.Http;

namespace HSMServer.Middleware
{
    // The pipeline-side writer of the uniform error contract (see ManagementApiErrorDto):
    // the single serializer every non-MVC error path of the management API goes through —
    // the area guard's 404s, the legacy bearer guard's 401, the HsmApiToken challenge and
    // the /api exception handler — so the wire shape cannot drift between enforcement
    // points. WriteAsJsonAsync produces the same web-default camelCase casing MVC uses
    // for the controller-side results. The per-kind helpers pre-assemble the
    // code↔status pairs so no caller can emit a mismatched one (the 404 body must stay
    // byte-identical everywhere — the area's anti-enumeration rule).
    public static class ManagementApiErrorResponses
    {
        public static Task WriteAsync(HttpContext context, int statusCode, string error, string message,
            object details = null)
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

        // 500 with the incident's trace id — the only field allowed to vary per incident.
        public static Task WriteInternalError(HttpContext context, string traceId) =>
            WriteAsync(context, StatusCodes.Status500InternalServerError,
                ManagementApiErrors.InternalErrorCode, "An unexpected error occurred.",
                new Dictionary<string, string> { ["traceId"] = traceId });
    }
}
