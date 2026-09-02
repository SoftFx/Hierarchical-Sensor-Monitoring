using System;
using System.Threading.Tasks;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Http;
using NLog;


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
                // An hsm_pat_ credential can surface inside an exception message (a
                // framework binding error echoing a header value). Redaction happens at
                // the NLog sink (nlog.config wraps message and exception text in
                // ${hsm-redacted}): the credential can reach a log target not only through
                // this record but through inner exceptions and through the outer exception
                // handlers' own logging, so no call-site rewriting can cover it all. This
                // middleware only logs the failure and rethrows.
                if (context.Response.HasStarted)
                {
                    _logger.Error(ex, "Exception occurred, but response was already started");
                }
                else
                {
                    _logger.Error(ex, $"Error in {context.Request.Method} {context.Request.Host} {context.Request.Path} {context.Request.Protocol} => {context.Response.StatusCode}");
                }

                throw;
            }
        }
    }
}
