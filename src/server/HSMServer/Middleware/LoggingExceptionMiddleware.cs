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
                if (context.Request.ContentLength > 10_000_000)
                {
                    context.Request.EnableBuffering();

                    using var sw = new StreamWriter(context.Request.Body);

                    using var fileStream = File.Create(Path.Combine(Environment.CurrentDirectory, "Logs", DateTime.UtcNow.ToString()));

                    context.Request.Body.Seek(0, SeekOrigin.Begin);
                    context.Request.Body.CopyTo(fileStream);
                }

                await _next(context);
            }
            catch (Exception ex)
            {
                if (context.Connection.LocalPort == ConfigurationConstants.SensorsPort)
                {
                    var request = context.Request;

                    _logger.Error($"Path {request.Path}, remote id, port: {context.Connection.RemoteIpAddress}:{context.Connection.RemotePort}, content size = {request.ContentLength}");


                    //var arr = new byte[1000];
                    //var cnt = await context.Request.Body.ReadAsync(arr, 0, 1000);

                    //var str64 = Base64.ToBase64String(arr, 0, cnt);


                    //_logger.Error(ex, Encoding.UTF8.GetString(arr, 0, cnt));
                }
                throw;
            }
        }
    }
}
