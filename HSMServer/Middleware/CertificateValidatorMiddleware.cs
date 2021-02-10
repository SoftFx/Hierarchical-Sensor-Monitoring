using System.Security.Claims;
using System.Threading.Tasks;
using HSMServer.Configuration;
using Microsoft.AspNetCore.Http;
using NLog;

namespace HSMServer.Middleware
{
    public class CertificateValidatorMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger _logger;
        private readonly ClientCertificateValidator _validator;

        public CertificateValidatorMiddleware(RequestDelegate next, ClientCertificateValidator validator)
        {
            _next = next;
            _validator = validator;
            _logger = LogManager.GetCurrentClassLogger();
            _logger.Info("Certificate validation middleware created.");
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var port = context.Connection.LocalPort;

            if (port == Config.GrpcPort)
            {
                var certificate = context.Connection.ClientCertificate;

                if (!_validator.IsValid(certificate))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    //await context.ForbidAsync();
                    return;
                }
            }

            await _next(context);
        }
    }
}
