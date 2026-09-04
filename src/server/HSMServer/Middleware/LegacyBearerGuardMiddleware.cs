using System;
using System.Linq;
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
    // a 500 over it. Since #1353 the 401 carries the area's uniform JSON error body, so a
    // client that misplaces its token gets a machine-readable answer here too.
    public sealed class LegacyBearerGuardMiddleware(RequestDelegate next)
    {
        public Task InvokeAsync(HttpContext context)
        {
            if (IsManagementAreaPath(context.Request.Path))
                return next(context);

            if (ContainsHsmBearer(context.Request.Headers))
                return ManagementApiErrorResponses.WriteUnauthorized(context,
                    "A management API token is not accepted on this route.");

            return next(context);
        }

        // Header work only: the shared bearer unpacking plus a prefix check, with no
        // interaction with the token store on any path this guard takes. Each duplicated
        // Authorization value is inspected on its own (a joined ", "-string would parse as
        // its first value's scheme and hide the credential), and the version-independent
        // family prefix is matched, so a future hsm_pat_v2_ credential cannot slip past
        // this guard into the legacy pipeline either.
        private static bool ContainsHsmBearer(IHeaderDictionary headers) =>
            headers.TryGetValue(HeaderNames.Authorization, out var values) &&
            values.Any(value => ApiTokenMaterial.TryReadBearerCredential(value, out var credential) &&
                                credential.StartsWith(ApiTokenMaterial.TokenFamilyPrefix, StringComparison.Ordinal));

        internal static bool IsManagementAreaPath(PathString path) =>
            path.StartsWithSegments(HsmApiTokenDefaults.ManagementAreaPath, StringComparison.OrdinalIgnoreCase);
    }
}
