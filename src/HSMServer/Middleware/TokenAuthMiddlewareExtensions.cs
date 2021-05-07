using Microsoft.AspNetCore.Builder;

namespace HSMServer.Middleware
{
    internal static class TokenAuthMiddlewareExtensions
    {
        public static IApplicationBuilder UseTokenAuth(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<TokenAuthMiddleware>();
        }
    }
}
