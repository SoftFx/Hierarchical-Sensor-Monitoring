using HSMCommon.Constants;
using Microsoft.AspNetCore.Http;
using NLog;
using System;
using System.IO;
using System.Threading.Tasks;


namespace HSMServer.Middleware
{
    internal sealed class LoggingExceptionMiddleware
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly RequestDelegate _next;

        public LoggingExceptionMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                if (context.Connection.LocalPort == ConfigurationConstants.SensorsPort)
                {
                    var request = context.Request;

                    request.EnableBuffering();

                    var body = await new StreamReader(context.Request.Body).ReadToEndAsync();

                    _logger.Error(ex, body);
                }

                throw;
            }
        }
    }
}
