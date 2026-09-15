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

                // /mcp speaks JSON-RPC, not the area's uniform contract: whatever
                // escapes the SDK handler answers a JSON-RPC INTERNAL ERROR object
                // so an MCP client can parse the failure and surface the trace id
                // (#1392 review); /api keeps the uniform three-field body. The
                // content type is set explicitly — WriteAsync does not do it, and
                // a content-type-dispatching client would discard the body (and
                // the trace id with it) as unparseable (#1392 review, round 4).
                if (IsMcpPath(context.Request.Path))
                {
                    context.Response.ContentType = "application/json";
                    await WriteJsonRpcInternalError(context, context.TraceIdentifier);
                    return;
                }

                await ManagementApiErrorResponses.WriteInternalError(context, context.TraceIdentifier);
            }
        }

        // The JSON-RPC 2.0 error envelope (code -32603 "Internal error"). The id
        // is null by necessity — the escaped exception killed the request before
        // the JSON-RPC layer could correlate it; data.traceId ties the failure to
        // the server log exactly like the uniform contract's details.traceId.
        // Serialized through an anonymous shape (property names verbatim — no
        // naming policy applies) so the escaping of the trace id is the
        // serializer's, never hand-rolled.
        private static Task WriteJsonRpcInternalError(HttpContext context, string traceId) =>
            // No RequestAborted token, deliberately: writing to a dead connection
            // must not replace the caught exception with a cancellation in the
            // global handler's logging (same reasoning as the filter above).
            context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id = (string)null,
                error = new
                {
                    code = -32603,
                    message = "Internal error",
                    data = new { traceId },
                },
            }));

        // Covers /api/v1 (management) and the sibling unauthenticated API families
        // (agent self-update, sensor data), plus the MCP endpoint — none of them
        // may answer HTML (#1392 review for the /mcp arm).
        private static bool IsApiPath(PathString path) =>
            path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) ||
            IsMcpPath(path);

        private static bool IsMcpPath(PathString path) =>
            path.StartsWithSegments(HsmMcp.EndpointPath, StringComparison.OrdinalIgnoreCase);
    }
}
