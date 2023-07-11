using HSMCommon.Constants;
using HSMSensorDataObjects;
using Microsoft.AspNetCore.Http;
using NLog;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace HSMServer.Middleware
{
    internal sealed class LoggingExceptionMiddleware
    {
        private const int MaxSizeForDeserializeContent = int.MaxValue;

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
                context.Request.EnableBuffering();

                await _next(context);
            }
            catch
            {
                if (context.Connection.LocalPort == ConfigurationConstants.SensorsPort)
                {
                    var request = context.Request;
                    request.Headers.TryGetValue(nameof(BaseRequest.Key), out var key);

                    _logger.Error($"Path {request.Path}, access key = {key}, remote id, port: {context.Connection.RemoteIpAddress}:{context.Connection.RemotePort}, content size = {request.ContentLength}");

                    if (request.ContentLength <= MaxSizeForDeserializeContent)
                    {
                        request.Body.Position = 0;

                        using StreamReader reader = new(request.Body, Encoding.UTF8, true, 1024, true);
                        var bodyStr = await reader.ReadToEndAsync();

                        _logger.Error($"Error request: {bodyStr}");

                        request.Body.Position = 0;
                    }
                }

                throw;
            }
        }
    }
}
