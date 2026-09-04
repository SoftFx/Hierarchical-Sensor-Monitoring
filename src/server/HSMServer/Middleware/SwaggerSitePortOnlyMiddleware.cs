using System;
using System.Threading.Tasks;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Http;

namespace HSMServer.Middleware
{
    // The OpenAPI document is worth reading since #1353 — it enumerates every
    // management route, its schemas and the credential format. The area guard hides
    // the management surface from the SensorPort listener ("responses do not confirm
    // that a management route exists"); serving /swagger/{version}/swagger.json and
    // the /api/swagger UI on that same port would publish the map the guard conceals.
    // This gate answers swagger paths off the SitePort with the area's uniform 404 and
    // lets everything else through untouched.
    public sealed class SwaggerSitePortOnlyMiddleware(RequestDelegate next, HsmListenerBindings listeners)
    {
        public Task InvokeAsync(HttpContext context)
        {
            if (IsSwaggerPath(context.Request.Path) && !listeners.IsSitePort(context.Connection.LocalPort))
                return ManagementApiErrorResponses.WriteNotFound(context);

            return next(context);
        }

        private static bool IsSwaggerPath(PathString path) =>
            path.StartsWithSegments("/swagger", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWithSegments("/api/swagger", StringComparison.OrdinalIgnoreCase);
    }
}
