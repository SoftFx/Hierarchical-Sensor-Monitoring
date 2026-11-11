using HSMServer.Core.Cache;
using HSMServer.Extensions;
using HSMServer.SensorApi.Sessions;
using Microsoft.AspNetCore.Http;
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
            else
                await context.SetAccessError(error);

            // A request that was upgraded is a collector session: its traffic was counted
            // frame by frame, and its HTTP response has no body to count.
            if (context.TryGetPublicApiInfo(out var requestInfo) && !SessionProtocol.WasUpgraded(context))
                _statistics.AddResponse(requestInfo.TelemetryPath, context.Response.ContentLength);
        }
    }
}
