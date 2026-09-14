using System;
using System.Threading.Tasks;
using HSMServer.Mcp;
using Microsoft.AspNetCore.Http;

namespace HSMServer.Middleware
{
    // HTML-off-the-wire for /api (#1353, epic #1347): the global exception handler
    // re-executes /Error, which renders Razor — a machine client would get an HTML
    // page for a 500. This middleware sits between the global handler and
    // LoggingExceptionMiddleware (the inner one logs first, then rethrows), catches
    // everything left on an /api or /mcp path and answers with the area's uniform
    // JSON error contract. Every other path rethrows untouched, so the Razor error
    // page keeps serving the browser UI; a started response cannot be rewritten and
    // rethrows too.
    public sealed class ApiExceptionJsonMiddleware(RequestDelegate next)
    {
        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await next(context);
            }
            // Cancellation is not a server error: an aborted request surfaces as
            // OperationCanceledException from the framework's body/abort plumbing, and
            // writing a 500 to a dead connection would only throw again and REPLACE the
            // original exception in the outer handler's logging. Let cancellations flow
            // to the global handlers untouched.
            catch (Exception exception) when (exception is not OperationCanceledException &&
                                              IsApiPath(context.Request.Path) &&
                                              !context.Response.HasStarted)
            {
                // Deliberately no exception text on the wire: the message carries no
                // internals, and the trace id is the key that locates the record
                // LoggingExceptionMiddleware has already written (its layout carries
                // ${aspnet-TraceIdentifier}).
                //
                // NOT Response.Clear(): headers set by outer middleware on the way in
                // (HSTS above all) must survive the 500; only the aborted response's
                // content staging is discarded, and the writer restages it.
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.Headers.ContentLength = default;
                context.Response.Headers.ContentType = default;

                await ManagementApiErrorResponses.WriteInternalError(context, context.TraceIdentifier);
            }
        }

        // Covers /api/v1 (management) and the sibling unauthenticated API families
        // (agent self-update, sensor data), plus the MCP endpoint — none of them
        // may answer HTML. On /mcp the JSON contract is not a JSON-RPC error body,
        // but it is the machine-readable answer where the alternative is the Razor
        // page for whatever escaped the SDK handler (#1392 review).
        private static bool IsApiPath(PathString path) =>
            path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWithSegments(HsmMcp.EndpointPath, StringComparison.OrdinalIgnoreCase);
    }
}
