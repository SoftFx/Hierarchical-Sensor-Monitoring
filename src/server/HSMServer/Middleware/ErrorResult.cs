using HSMSensorDataObjects.SensorValueRequests;
using HSMServer.BackgroundServices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.Middleware
{
    public class Response
    {
        public IEnumerable<string> Errors { get; set; }
    }

    public class Response<T> : Response
    {
        public T Data { get; set; }
    }

    public class ErrorResultFilter : IAsyncActionFilter, IAsyncResultFilter
    {
        private readonly ClientStatistics _statistics;
        public ErrorResultFilter(ClientStatistics statistics)
        {
            _statistics = statistics;

        }
        
        public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
        {
            var result = context.Result as ObjectResult;

            //if error do nothing
            //if ok add receive value sensor
            
            if (result?.Value is BadRequestObjectResult)
            {
                Console.WriteLine("do not increase counter");
            }

            await next();
        }
        
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var result = context.Result as ObjectResult;

            //add receive
            if (result?.Value is BadRequestObjectResult)
            {
                Console.WriteLine("do not increase counter");
            }

            _statistics[context.HttpContext.Request.Headers["Path"]].AddReceiveData(context);
            _statistics.Total.AddReceiveData(context);
         
            await next();
        }
    }
}
