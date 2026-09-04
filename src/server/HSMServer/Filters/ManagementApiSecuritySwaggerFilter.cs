using System;
using HSMServer.Authentication;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace HSMServer.Filters
{
    /// <summary>
    /// Attaches the HsmApiToken bearer requirement to MANAGEMENT operations only
    /// (#1353, epic #1347). The single swagger doc also carries the sensor-data API
    /// (Key header) and unauthenticated routes, so a global security requirement would
    /// misdescribe them; membership here is the same controller marker the area guard
    /// admits endpoints by. The scheme itself is defined in AddSwaggerGen under
    /// <see cref="SchemeName"/> — an agent that reads only the spec learns from it how
    /// to authenticate.
    /// </summary>
    public sealed class ManagementApiSecuritySwaggerFilter : IOperationFilter
    {
        public const string SchemeName = "HsmApiToken";


        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            // inherit: true — endpoint metadata (what the runtime guard reads) includes
            // attributes inherited from a base controller; this filter must not
            // silently diverge from it.
            if (context.MethodInfo?.DeclaringType is not { } controller ||
                !controller.IsDefined(typeof(ManagementApiAttribute), inherit: true))
                return;

            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Id = SchemeName,
                        Type = ReferenceType.SecurityScheme,
                    },
                }] = Array.Empty<string>(),
            });
        }
    }
}
