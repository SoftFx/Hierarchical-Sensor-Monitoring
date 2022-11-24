using HSMCommon.Constants;
using Microsoft.AspNetCore.Http;
using NLog;
using Org.BouncyCastle.Utilities.Encoders;
using System;
using System.Text;
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

                    _logger.Error($"Path {request.Path}, remote port: {context.Connection.RemotePort}");


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
