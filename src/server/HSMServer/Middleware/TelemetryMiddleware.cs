using HSMSensorDataObjects;
using HSMServer.BackgroundServices;
using HSMServer.Core.Cache;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace HSMServer.Middleware
{
    public class TelemetryMiddleware
    {
        private const double KbDivisor = 1 << 10;
        
        private readonly RequestDelegate _next;
        private readonly DataCollectorWrapper _collector;
        private readonly ITreeValuesCache _cache;


        public TelemetryMiddleware(RequestDelegate next, DataCollectorWrapper collector, ITreeValuesCache cache)
        {
            _collector = collector;
            _cache = cache;
            _next = next;
        }

        public Task InvokeAsync(HttpContext context)
        {
            var request = context.Request;
            request.Headers.TryGetValue(nameof(BaseRequest.Key), out var key);

            if (_cache.TryGetProductByKey(key.ToString(), out var product, out var keyModel))
            {
                var collectorName = request.Headers.TryGetValue(nameof(BaseRequest.ClientName), out var clientName) && !string.IsNullOrWhiteSpace(clientName) ? clientName.ToString() : "No name";
                var path = $"{product.DisplayName}/{keyModel.DisplayName}/{collectorName}";
                context.Request.Headers["Path"] = path;
                _collector.Statistics[path].AddRequestData(request);
            }
            
            _collector.Statistics.Total.AddRequestData(request);
            
            context.Response.OnCompleted(() =>
            {
                _collector.Statistics.Total.AddResponseResult(context.Response);
                if (context.Request.Headers.TryGetValue("Path", out var path))
                {
                    _collector.Statistics[path.ToString()].AddResponseResult(context.Response);
                }
                return Task.CompletedTask;
            });

            return _next(context);
        }
    }
}
