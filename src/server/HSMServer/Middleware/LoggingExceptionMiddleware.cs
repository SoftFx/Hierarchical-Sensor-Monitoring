using System;
using System.Threading.Tasks;
using HSMServer.Authentication;
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
                // framework binding error echoing a header value). Redact before logging:
                // the credential is a password-class secret and must never reach a log.
                // Only messages that actually carry the prefix are wrapped, so ordinary
                // exceptions keep their exact type and text.
                var logged = ex.Message.Contains(ApiTokenMaterial.TokenPrefix, StringComparison.Ordinal)
                    ? new Exception(ApiTokenMaterial.Redact(ex.Message), ex)
                    : ex;

                if (context.Response.HasStarted)
                {
                    _logger.Error(logged, "Exception occurred, but response was already started");
                }
                else
                {
                    _logger.Error(logged, $"Error in {context.Request.Method} {context.Request.Host} {context.Request.Path} {context.Request.Protocol} => {context.Response.StatusCode}", logged);
                }

                throw;
            }
        }
    }
}
