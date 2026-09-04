using System.Linq;
using System.Reflection;
using HSMSensorDataObjects;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace HSMServer.Filters
{
    /// <summary>
    /// Swagger filter for adding required string 'Key' in API requests header.
    /// Scoped (#1353) to the sensor-data actions the header actually belongs to: the
    /// single swagger doc also carries the management API, which authenticates through
    /// the Authorization bearer header and must not advertise a Key header.
    /// </summary>
    public sealed class DataRequestHeaderSwaggerFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            if (context.MethodInfo is null || !CarriesBaseRequest(context.MethodInfo))
                return;

            operation.Parameters ??= [];

            operation.Parameters.Add(new OpenApiParameter
            {
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

        // The Key/ClientName headers describe actions whose request body is (or derives
        // from) BaseRequest — the sensor-data API family.
        private static bool CarriesBaseRequest(MethodInfo method) =>
            method.GetParameters().Any(parameter => typeof(BaseRequest).IsAssignableFrom(parameter.ParameterType));
    }
}
