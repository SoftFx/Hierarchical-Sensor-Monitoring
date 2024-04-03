using HSMServer.BackgroundServices;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace HSMServer.Middleware
{
    public sealed class TelemetryMiddleware(RequestDelegate next)
    {
        public const string RequestData = "RequestData";
        
        public async Task InvokeAsync(HttpContext context, DataCollectorWrapper collector)
        {
            context.Items.Add(RequestData, new RequestData());

            collector.WebRequestsSensors.Total.AddRequestData(context.Request);

            await next(context);
            
            collector.WebRequestsSensors.Total.AddResponseResult(context.Response);
            
            if (context.Items.TryGetValue(RequestData, out var value) && value is RequestData requestData)
                collector.WebRequestsSensors.Total.AddReceiveData(requestData.Count);
        }
    }
}
