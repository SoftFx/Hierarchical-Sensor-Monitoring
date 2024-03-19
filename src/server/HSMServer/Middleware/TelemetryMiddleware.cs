using HSMServer.BackgroundServices;
using HSMServer.Core.Cache;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace HSMServer.Middleware
{
    public class TelemetryMiddleware(RequestDelegate next, DataCollectorWrapper collector, ITreeValuesCache cache)
    {
        public const string RequestData = "RequestData";
        
        public async Task InvokeAsync(HttpContext context)
        {
            context.Items.Add(RequestData, new FilterRequestData());
            
            collector.Statistics.Total.AddRequestData(context.Request);
            
            await next(context);
            
            collector.Statistics.Total.AddResponseResult(context.Response);
            collector.Statistics.Total.AddReceiveData((int)context.Items["SensorsCount"]);
        }
    }
}
