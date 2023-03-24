using HSM.Core.Monitoring;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace HSMServer.Middleware
{
    internal class RequestStatisticsMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IDataCollectorFacade _dataCollector;

        public RequestStatisticsMiddleware(RequestDelegate next, IDataCollectorFacade dataCollector)
        {
            _next = next;
            _dataCollector = dataCollector;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            _dataCollector.IncreaseRequestsCount();

            var request = context.Request;
            _dataCollector.ReportRequestSize(request.ContentLength ?? 0);

            context.Response.OnCompleted(() =>
            {
                _dataCollector.ReportResponseSize(context.Response.ContentLength ?? 0);
                return Task.CompletedTask;
            });

            await _next(context);
        }
    }
}
