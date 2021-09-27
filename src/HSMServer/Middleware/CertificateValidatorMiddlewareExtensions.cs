using System;
using Microsoft.AspNetCore.Builder;

namespace HSMServer.Middleware
{
    [Obsolete("15.09.2021. Remove desktop client and certificate auth")]
    internal static class CertificateValidatorMiddlewareExtensions
    {
        public static IApplicationBuilder UseCertificateValidator(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<CertificateValidatorMiddleware>();
        }
    }
}
