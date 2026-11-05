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

                // #1632: a client dropping the connection before the response finishes
                // (a polling MCP client abandoning its POST /mcp) surfaces as an
                // OperationCanceledException (incl. TaskCanceledException) out of the
                // transport, with or without the response started. ClientAbortDetection
                // classifies it by cause — the aborted request token, or the transport
                // itself reporting the dead connection: not a server fault an operator
                // must look at, so it logs at Warn and stays out of HSM-error-log.txt
                // (the operator surface, ADR-0016) while still reaching HSM-all-log.txt
                // via the Warn+ rule. An OperationCanceledException with the request
                // token intact (an internal timeout or cancellation) is not a client
                // disconnect and stays an Error.
                if (ClientAbortDetection.IsClientAbort(ex, context))
                {
                    _logger.Warn(ex, $"Client cancelled {context.Request.Method} {context.Request.Path} request before it completed");
                }
                else if (context.Response.HasStarted)
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
