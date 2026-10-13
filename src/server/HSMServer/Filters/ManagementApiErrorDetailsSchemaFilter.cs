using HSMServer.Model.ManagementApi;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace HSMServer.Filters
{
    // The typed details union of the uniform error contract (#1371): Details is
    // declared Dictionary<string, object> (System.Text.Json serializes the values
    // by runtime type, which is what keeps the wire contract unchanged), which
    // Swashbuckle would publish as an object with untyped values — an agent
    // bootstrapping from the OpenAPI document alone would never learn which value
    // shapes exist. This filter publishes the two actual shapes explicitly, the
    // same treatment SensorValueDto.Value got in #1387 review round 4: 400/422
    // details map fields to message ARRAYS, while the 500 trace pointer and the
    // 409 resource pointers map keys to scalar STRINGS. The XML remarks on the
    // property remain the per-status narrative; this makes the union
    // machine-readable.
    public sealed class ManagementApiErrorDetailsSchemaFilter : ISchemaFilter
    {
        public void Apply(OpenApiSchema schema, SchemaFilterContext context)
        {
            if (context.Type != typeof(ManagementApiErrorDto))
                return;

            if (!schema.Properties.TryGetValue("details", out var detailsSchema))
                return;

            // Null on 401/403/404/409-without-pointers/503 — the field is always
            // present on the wire, but half the statuses answer an explicit null.
            detailsSchema.Nullable = true;
            detailsSchema.Type = null;
            detailsSchema.AdditionalProperties = null;
            detailsSchema.AdditionalPropertiesAllowed = false;

            detailsSchema.OneOf =
            [
                // 400/422: field-keyed message arrays ({"policies[0].operation": ["..."]}).
                new OpenApiSchema
                {
                    Type = "object",
                    AdditionalProperties = new OpenApiSchema
                    {
                        Type = "array",
                        Items = new OpenApiSchema { Type = "string" },
                    },
                },
                // 500 {"traceId": "..."} and create-path 409 resource pointers
                // ({"templateId": "..."}).
                new OpenApiSchema
                {
                    Type = "object",
                    AdditionalProperties = new OpenApiSchema { Type = "string" },
                },
            ];
        }
    }
}
