using HSMServer.Model.ManagementApi.SensorTree;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace HSMServer.Filters
{
    // The typed value union of the sensor-tree surface (#1387 review, round 4):
    // SensorValueDto.Value is declared `object` (System.Text.Json serializes the
    // runtime type), which Swashbuckle would publish as a free-form `{}` — an
    // agent bootstrapping from the OpenAPI document alone would never learn the
    // per-type shapes, the very thing the feature is about. This filter maps the
    // property to an explicit oneOf: the three structured shapes (Enum/Bar/File
    // metadata) as component schemas, plus the scalar primitives. The narrative
    // tables in the XML remarks remain the value-table source of truth; this
    // makes them machine-readable.
    public sealed class SensorValueUnionSchemaFilter : ISchemaFilter
    {
        public void Apply(OpenApiSchema schema, SchemaFilterContext context)
        {
            if (context.Type != typeof(SensorValueDto))
                return;

            if (!schema.Properties.TryGetValue("value", out var valueSchema))
                return;

            valueSchema.OneOf =
            [
                context.SchemaGenerator.GenerateSchema(typeof(EnumValueDto), context.SchemaRepository),
                context.SchemaGenerator.GenerateSchema(typeof(BarValueDto), context.SchemaRepository),
                context.SchemaGenerator.GenerateSchema(typeof(FileValueDto), context.SchemaRepository),
                new OpenApiSchema { Type = "number", Nullable = true },
                new OpenApiSchema { Type = "string", Nullable = true },
                new OpenApiSchema { Type = "boolean", Nullable = true },
            ];

            valueSchema.Type = null;
            valueSchema.AdditionalPropertiesAllowed = false;
        }
    }
}
