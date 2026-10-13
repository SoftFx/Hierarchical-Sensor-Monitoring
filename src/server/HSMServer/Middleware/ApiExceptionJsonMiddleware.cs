using System;
using System.Threading.Tasks;
using HSMServer.Mcp;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

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
            // Cancellation is not a server error ONLY when the CLIENT is gone: an
            // aborted request surfaces as OperationCanceledException from the
            // framework's body/abort plumbing, and writing a 500 to a dead
            // connection would only throw again and REPLACE the original exception
            // in the outer handler's logging. A server-side cancel — e.g. an
            // HttpClient timeout inside a handler throws OperationCanceledException
            // while RequestAborted is NOT set — is an ordinary failure of a live
            // request: it must render the JSON error on /api paths, not fall
            // through to the Razor error page (#1371).
            catch (Exception exception) when (!(exception is OperationCanceledException &&
                                                context.RequestAborted.IsCancellationRequested) &&
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
                // representation is discarded, and the writer restages its content
                // headers (#1371: a staged Content-Encoding — br over the
                // uncompressed JSON — or a validator — ETag — describing the OLD
                // body would corrupt or mis-validate the rewritten one).
                ResetAbortedResponse(context.Response);

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

        // The staged representation of the aborted response is discarded wholesale:
        // the 500 replaces the body's bytes, so every header DESCRIBING those bytes
        // goes with them — the two the JSON writer restages (Content-Length,
        // Content-Type), the content metadata that would corrupt the new body's
        // decoding (a staged Content-Encoding: br would label the uncompressed JSON
        // as compressed), and the validators of the old representation (ETag,
        // Last-Modified — they would "validate" bytes that no longer exist).
        // Headers describing the transaction rather than the bytes (HSTS, Server,
        // Cache-Control, …) survive — the same reason this is not Response.Clear().
        private static void ResetAbortedResponse(HttpResponse response)
        {
            response.StatusCode = StatusCodes.Status500InternalServerError;

            response.Headers.ContentLength = default;
            response.Headers.ContentType = default;
            response.Headers.Remove(HeaderNames.ContentEncoding);
            response.Headers.Remove(HeaderNames.ContentLanguage);
            response.Headers.Remove(HeaderNames.ContentLocation);
            response.Headers.Remove(HeaderNames.ContentRange);
            response.Headers.Remove(HeaderNames.ETag);
            response.Headers.Remove(HeaderNames.LastModified);
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
