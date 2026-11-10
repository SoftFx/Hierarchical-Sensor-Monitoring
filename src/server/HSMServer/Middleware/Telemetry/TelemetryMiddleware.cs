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
            // Discovery of collector sessions (#1664): every answer to a Sensor API request
            // says the server has them, so a collector never has to probe. Set before the
            // request runs, so refusals carry it too.
            if (_advertiseSessions && context.Request.Headers.ContainsKey(SessionProtocol.KeyHeader))
                context.Response.Headers[SessionProtocol.DiscoveryHeader] = SessionProtocol.Subprotocol;

            var result = await TryRegisterPublicApiRequest(context);

            if (result)
                await _next(context);

            // An upgraded request is a collector session: its traffic was counted frame by
            // frame, and its HTTP response has no body to count.
            if (context.TryGetPublicApiInfo(out var requestInfo) && !context.WebSockets.IsWebSocketRequest)
                _statistics.AddResponse(requestInfo.TelemetryPath, context.Response.ContentLength);
        }
    }
}
