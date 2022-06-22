using Microsoft.AspNetCore.Builder;

namespace HSMServer.Middleware
{
    public static class UserProcessorMiddlewareExtensions
    {
        public static IApplicationBuilder UseUserProcessor(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<UserProcessorMiddleware>();
        }
    }
}
