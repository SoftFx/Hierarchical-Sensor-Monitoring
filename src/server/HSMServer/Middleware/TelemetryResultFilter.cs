using HSMServer.BackgroundServices;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Threading.Tasks;

namespace HSMServer.Middleware
{
    public class TelemetryResultFilter(ClientStatistics statistics) : IAsyncActionFilter, IAsyncResultFilter
    {
        public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
        {
            await next();
        }
        
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            statistics[context.HttpContext.Request.Headers["Path"]].AddReceiveData(context);
            statistics.Total.AddReceiveData(context);
         
            await next();
        }
    }
}
