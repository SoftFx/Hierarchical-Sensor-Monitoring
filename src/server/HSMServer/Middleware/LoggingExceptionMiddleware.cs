using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using NLog;
using HSMServer.ServerConfiguration;


namespace HSMServer.Middleware
{
    internal sealed class LoggingExceptionMiddleware(RequestDelegate next, IServerConfig config)
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly RequestDelegate _next = next;


        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                _logger.Error($"Error during executing {context.Request.Method} {context.Request.Host} {context.Request.Path} {context.Request.Protocol} => {context.Response.StatusCode}", ex);

                throw;
            }
        }
    }
}
