using HSMServer.BackgroundServices;
using HSMServer.Core.Cache;
using HSMServer.Extensions;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace HSMServer.Middleware.Telemetry
{
    public sealed class TelemetryMiddleware(RequestDelegate _next, DataCollectorWrapper _collector, ITreeValuesCache _cache) : TelemetryCollector(_collector, _cache)
    {
        public async Task InvokeAsync(HttpContext context)
        {
            var result = await TryRegisterPublicApiRequest(context);

            if (result)
                await _next(context);

            if (context.TryGetPublicApiInfo(out var requestInfo))
            {
                _statistics.Total.AddResponseResult(context.Response);
                _statistics[requestInfo.TelemetryPath].AddResponseResult(context.Response);
            }
        }
    }
}