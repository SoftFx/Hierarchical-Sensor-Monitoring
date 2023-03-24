using HSM.Core.Monitoring;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace HSMServer.Middleware
{
    internal sealed class RequestStatisticsMiddleware
    {
        private readonly IDataCollectorFacade _dataCollector;
        private readonly RequestDelegate _next;

        public RequestStatisticsMiddleware(RequestDelegate next, IDataCollectorFacade dataCollector)
        {
            _next = next;
            _dataCollector = dataCollector;
        }

        public Task InvokeAsync(HttpContext context)
        {
            _dataCollector.IncreaseRequestsCount();

            var request = context.Request;
            _dataCollector.ReportRequestSize(request.ContentLength ?? 0);

            context.Response.OnCompleted(() =>
            {
                _dataCollector.ReportResponseSize(context.Response.ContentLength ?? 0);
                return Task.CompletedTask;
            });

            return _next(context);
        }
    }
}
