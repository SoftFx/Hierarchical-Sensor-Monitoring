using HSMServer.BackgroundServices;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace HSMServer.Middleware
{
    public class TelemetryMiddleware(RequestDelegate next)
    {
        public const string RequestData = "RequestData";
        
        public async Task InvokeAsync(HttpContext context, DataCollectorWrapper collector)
        {
            context.Items.Add(RequestData, new RequestData());
            
            collector.Statistics.Total.AddRequestData(context.Request);
            
            await next(context);
            
            collector.Statistics.Total.AddResponseResult(context.Response);
            
            if (context.Items.TryGetValue(RequestData, out var value) && value is RequestData requestData)
                collector.Statistics.Total.AddReceiveData(requestData.Count);
        }
    }
}
