using System.Reflection;
using HSMSensorDataObjects;
using HSMServer.Authentication;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace HSMServer.Filters
{
    /// <summary>
    /// Swagger filter for adding required string 'Key' in API requests header.
    /// Historically global; since #1353 the single exclusion is the management API —
    /// its operations authenticate through the Authorization bearer header and must
    /// not advertise a Key header. The rule is deliberately negative (skip
    /// [ManagementApi] controllers, add otherwise): several non-management families
    /// whose request types do NOT derive from BaseRequest also authenticate through
    /// the Key header (the Grafana JSON datasource above all — TryGetKey reads
    /// Request.Headers["Key"] exclusively), so a positive "parameter derives from
    /// BaseRequest" match would silently strip their credential from the spec.
    /// </summary>
    public sealed class DataRequestHeaderSwaggerFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            if (context.MethodInfo?.DeclaringType is not { } controller ||
                controller.IsDefined(typeof(ManagementApiAttribute), inherit: true))
                return;

            operation.Parameters ??= [];

            operation.Parameters.Add(new OpenApiParameter
            {
                // nameof: the compile-time tie to the DTO property the header carries —
                // renaming BaseRequest.Key must not silently desync the spec.
                Name = nameof(BaseRequest.Key),
                In = ParameterLocation.Header,
                Required = true,
                Schema = new OpenApiSchema
                {
                    Type = "string",
                }
            });

            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "ClientName",
                In = ParameterLocation.Header,
                Required = false,
                Schema = new OpenApiSchema
                {
                    Type = "string",
                }
            });
        }
    }
}
