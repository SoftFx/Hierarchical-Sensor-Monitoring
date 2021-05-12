using System.Threading.Tasks;
using HSMServer.Authentication;
using HSMServer.Configuration;
using HSMServer.Constants;
using Microsoft.AspNetCore.Http;

namespace HSMServer.Middleware
{
    internal class CertificateValidatorMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ClientCertificateValidator _validator;
        private readonly UserManager _userManager;

        public CertificateValidatorMiddleware(RequestDelegate next, ClientCertificateValidator validator, UserManager userManager)
        {
            _next = next;
            _validator = validator;
            _userManager = userManager;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var port = context.Connection.LocalPort;

            if (port == ConfigurationConstants.GrpcPort)
            {
                var certificate = context.Connection.ClientCertificate;

                if (!_validator.IsValid(certificate))
                {
                    //_logger.Warn($" Denied access for {certificate.Thumbprint} : {certificate.SubjectName.Name}");
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    //await context.ForbidAsync();
                    return;
                }

                context.User = _userManager.GetUserByCertificateThumbprint(certificate.Thumbprint);
            }

            await _next(context);
        }
    }
}
