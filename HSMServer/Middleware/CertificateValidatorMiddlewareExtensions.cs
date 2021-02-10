using Microsoft.AspNetCore.Builder;

namespace HSMServer.Middleware
{
    public static class CertificateValidatorMiddlewareExtensions
    {
        public static IApplicationBuilder UseCertificateValidator(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<CertificateValidatorMiddleware>();
        }
    }
}
