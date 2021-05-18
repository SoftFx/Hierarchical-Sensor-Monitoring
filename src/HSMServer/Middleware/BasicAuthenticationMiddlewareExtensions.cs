using Microsoft.AspNetCore.Builder;

namespace HSMServer.Middleware
{
    internal static class BasicAuthenticationMiddlewareExtensions
    {
        public static IApplicationBuilder UseBasicAuthentication(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<BasicAuthenticationMiddleware>();
        }
    }
}
