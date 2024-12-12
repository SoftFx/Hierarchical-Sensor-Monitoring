using System.IO;
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
            using(var buffer = new MemoryStream())
            {
                var result = await TryRegisterPublicApiRequest(context);

                var response = context.Response;

                var bodyStream = response.Body;
                response.Body = buffer;

                if (result)
                    await _next(context);

                response.ContentLength = buffer.Length;
                buffer.Position = 0;

                await buffer.CopyToAsync(bodyStream);
            }

            if (context.TryGetPublicApiInfo(out var requestInfo))
            {
                _statistics.Total.AddResponseResult(context.Response);
                _statistics[requestInfo.TelemetryPath].AddResponseResult(context.Response);
            }
        }
    }
}