using HSMServer.Core.Cache;
using HSMServer.Extensions;
using HSMServer.SensorApi.Sessions;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace HSMServer.Middleware.Telemetry
{
    public sealed class TelemetryMiddleware(RequestDelegate _next, ISensorApiTelemetry _telemetry, ITreeValuesCache _cache)
        : TelemetryCollector(_telemetry, _cache)
    {
        public async Task InvokeAsync(HttpContext context)
        {
            var result = await TryRegisterPublicApiRequest(context);

            if (result)
                await _next(context);

            // A request that was upgraded is a collector session: its traffic was counted
            // frame by frame, and its HTTP response has no body to count.
            if (context.TryGetPublicApiInfo(out var requestInfo) && !SessionProtocol.WasUpgraded(context))
                _statistics.AddResponse(requestInfo.TelemetryPath, context.Response.ContentLength);
        }
    }
}
