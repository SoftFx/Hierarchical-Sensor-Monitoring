using Microsoft.AspNetCore.Builder;

namespace HSMServer.Middleware
{
    public static class RequestStatisticsMiddlewareExtensions
    {
        public static IApplicationBuilder CountRequestStatistics(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<RequestStatisticsMiddleware>();
        }
    }
}
