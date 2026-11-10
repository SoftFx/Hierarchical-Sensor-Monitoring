using HSMServer.Core.Cache;
using HSMServer.Extensions;
using HSMServer.SensorApi.Sessions;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace HSMServer.Middleware.Telemetry
{
    public sealed class TelemetryMiddleware(RequestDelegate _next, ISensorApiTelemetry _telemetry, ITreeValuesCache _cache, IServerConfig _config)
        : TelemetryCollector(_telemetry, _cache)
    {
        private readonly bool _advertiseSessions = _config.CollectorSessions?.Enabled ?? false;


        public async Task InvokeAsync(HttpContext context)
        {
            // Discovery of collector sessions (#1664): every answer to a request that states
            // its access key in the Key header says the server has them, so a collector never
            // has to probe. Set before the request runs, so refusals carry it too. A request
            // with the key in its body (the pre-v4 form) does not get it: such a collector
            // knows nothing of sessions.
            if (_advertiseSessions && context.Request.Headers.ContainsKey(SessionProtocol.KeyHeader))
                context.Response.Headers[SessionProtocol.DiscoveryHeader] = SessionProtocol.Subprotocol;

            var result = await TryRegisterPublicApiRequest(context);

            if (result)
                await _next(context);

            // A request that was upgraded is a collector session: its traffic was counted
            // frame by frame, and its HTTP response has no body to count.
            if (context.TryGetPublicApiInfo(out var requestInfo) && context.Response.StatusCode != StatusCodes.Status101SwitchingProtocols)
                _statistics.AddResponse(requestInfo.TelemetryPath, context.Response.ContentLength);
        }
    }
}
