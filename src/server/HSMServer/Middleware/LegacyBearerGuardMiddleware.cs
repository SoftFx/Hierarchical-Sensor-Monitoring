using System;
using System.Threading.Tasks;
using HSMServer.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace HSMServer.Middleware
{
    // Fail-closed isolation guard (initiative step 3): an hsm_pat_ bearer credential sent
    // to anything outside /api/v1 is rejected here, before MVC/Razor controller execution.
    // It performs no token lookup — the credential material only matters to the HsmApiToken
    // scheme — and answers a generic non-redirecting 401 so a legacy [Authorize] route can
    // never redirect it to the login page and a BaseController cast can never explode into
    // a 500 over it.
    public sealed class LegacyBearerGuardMiddleware(RequestDelegate next)
    {
        public Task InvokeAsync(HttpContext context)
        {
            if (IsManagementAreaPath(context.Request.Path))
                return next(context);

            if (ContainsHsmBearer(context.Request.Headers))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            return next(context);
        }

        // Header work only: the shared bearer unpacking plus a prefix check, with no
        // interaction with the token store on any path this guard takes.
        private static bool ContainsHsmBearer(IHeaderDictionary headers) =>
            headers.TryGetValue(HeaderNames.Authorization, out var value) &&
            ApiTokenMaterial.TryReadBearerCredential(value.ToString(), out var credential) &&
            credential.StartsWith(ApiTokenMaterial.TokenPrefix, StringComparison.Ordinal);

        internal static bool IsManagementAreaPath(PathString path) =>
            path.StartsWithSegments(HsmApiTokenDefaults.ManagementAreaPath, StringComparison.OrdinalIgnoreCase);
    }
}
