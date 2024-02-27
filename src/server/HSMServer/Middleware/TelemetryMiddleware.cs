using HSMSensorDataObjects;
using HSMServer.BackgroundServices;
using HSMServer.Core.Cache;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace HSMServer.Middleware
{
    public class TelemetryMiddleware(RequestDelegate next, DataCollectorWrapper collector, ITreeValuesCache cache)
    {
        private const double KbDivisor = 1 << 10;


        public Task InvokeAsync(HttpContext context)
        {
            // var request = context.Request;
            // request.Headers.TryGetValue(nameof(BaseRequest.Key), out var key);
            //
            // if (cache.TryGetProductByKey(key.ToString(), out var product, out var keyModel))
            // {
            //     var collectorName = request.Headers.TryGetValue(nameof(BaseRequest.ClientName), out var clientName) && !string.IsNullOrWhiteSpace(clientName) ? clientName.ToString() : "No name";
            //     var path = $"{product.DisplayName}/{keyModel.DisplayName}/{collectorName}";
            //     context.Request.Headers["Path"] = path;
            //     collector.Statistics[path].AddRequestData(request);
            // }
            // // above is public api, should be in keypermission filter ?
            //
            // collector.Statistics.Total.AddRequestData(request);
            //
            // context.Response.OnCompleted(() =>
            // {
            //     collector.Statistics.Total.AddResponseResult(context.Response);
            //     if (context.Request.Headers.TryGetValue("Path", out var path))
            //     {
            //         collector.Statistics[path.ToString()].AddResponseResult(context.Response);
            //     }
            //     return Task.CompletedTask;
            // });

            return next(context);
        }
    }
}
