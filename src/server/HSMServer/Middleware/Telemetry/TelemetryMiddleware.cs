using HSMServer.Core.Cache;
using HSMServer.Extensions;
using HSMServer.SensorApi.Sessions;
using Microsoft.AspNetCore.Http;
using System;
using System.Threading.Tasks;

namespace HSMServer.Middleware.Telemetry
{
    // Not sealed: the middleware tests stub the registration decision
    // (#1649) — the real collector needs the self-monitoring collector stack.
    public class TelemetryMiddleware(RequestDelegate _next, ISensorApiTelemetry _telemetry, ITreeValuesCache _cache)
        : TelemetryCollector(_telemetry, _cache)
    {
        public async Task InvokeAsync(HttpContext context)
        {
            // A rejected key (unknown, expired, blocked, missing permission)
            // is answered HERE, before MVC runs — nothing later formats a
            // result over the written response (#1649). Everything else —
            // including requests with no Key header at all — continues down
            // the pipeline.
            if (TryRegisterPublicApiRequest(context, out var error))
                await _next(context);
            else if (context.HasPublicApiFault() && IsRegistrationRoute(context.Request))
            {
                // The key was not rejected: the server failed while resolving it (#1756).
                // A collector takes the 406 of the access error on a registration as a
                // final refusal and never sends the package again; a 500 it sends again.
                // Only the two registration routes: every other route answers the access
                // error as before (a collector sends a value batch again whatever the
                // status, and the session upgrade is tried again too).
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;

                await context.Response.WriteAsync(error);
            }
            else
                await context.SetAccessError(error);

            // A request that was upgraded is a collector session: its traffic was counted
            // frame by frame, and its HTTP response has no body to count.
            if (context.TryGetPublicApiInfo(out var requestInfo) && !SessionProtocol.WasUpgraded(context))
                _statistics.AddResponse(requestInfo.TelemetryPath, context.Response.ContentLength);
        }

        // POST /api/sensors/commands and POST /api/sensors/addOrUpdate, the routes of
        // SensorsController a collector posts registrations to.
        private static bool IsRegistrationRoute(HttpRequest request) =>
            HttpMethods.IsPost(request.Method) &&
            (IsRoute(request.Path, "/api/sensors/commands") || IsRoute(request.Path, "/api/sensors/addOrUpdate"));

        private static bool IsRoute(PathString path, string route) =>
            path.StartsWithSegments(route, StringComparison.OrdinalIgnoreCase, out var rest) && (!rest.HasValue || rest.Value == "/");
    }
}
