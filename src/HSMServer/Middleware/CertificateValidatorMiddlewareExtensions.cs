using Microsoft.AspNetCore.Builder;

namespace HSMServer.Middleware
{
    internal static class CertificateValidatorMiddlewareExtensions
    {
        public static IApplicationBuilder UseCertificateValidator(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<CertificateValidatorMiddleware>();
        }
    }
}
