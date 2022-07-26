using HSMSensorDataObjects.Swagger;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace HSMServer.Filters
{
    public class SwaggerIgnoreClassFilter : IDocumentFilter
    {
        private static readonly List<Type> _excludedTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes()).Where(type =>
                type.GetCustomAttributes(true).OfType<SwaggerIgnoreAttribute>().Any()).ToList();

        public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
        {
            foreach (var typeToExclude in _excludedTypes)
            {
                swaggerDoc.Components.Schemas.Remove(typeToExclude.Name);
            }

            foreach (var description in context.ApiDescriptions)
            {
                description.TryGetMethodInfo(out var info);

                var devAttr = info.GetCustomAttributes(true)
                                  .OfType<SwaggerIgnoreAttribute>()
                                  .Distinct();

                if (devAttr.Any())
                {
                    string path = description.RelativePath;

                    var removeRoutes = swaggerDoc.Paths.Keys.Where(u => u.ToLower().EndsWith(path.ToLower()));

                    foreach (var p in removeRoutes)
                        swaggerDoc.Paths.Remove(p);
                }
            }
        }
    }

    public class SwaggerExcludePropertiesFilter : ISchemaFilter
    {
        public void Apply(OpenApiSchema schema, SchemaFilterContext context)
        {
            if (schema?.Properties == null)
                return;

            var ignoreDataMemberProperties = context.Type.GetProperties().Where(t => t.GetCustomAttributes(true).OfType<SwaggerExcludeAttribute>().Any());

            foreach (var ignoreProperty in ignoreDataMemberProperties) //apply ignore attribute
            {
                var name = ignoreProperty.Name.ToLower();
                var swaggerName = schema.Properties.Keys.FirstOrDefault(u => string.Equals(u, name, StringComparison.OrdinalIgnoreCase));

                if (swaggerName != null)
                    schema.Properties.Remove(swaggerName);
            }

            foreach (var propertyInfo in context.Type.GetProperties()) //apply default value attribute
            {
                var defaultAttribute = propertyInfo.GetCustomAttribute<DefaultValueAttribute>();

                if (defaultAttribute != null)
                {
                    var propName = propertyInfo.Name;
                    var swaggerName = schema.Properties.Keys.FirstOrDefault(u => string.Equals(u, propName, StringComparison.OrdinalIgnoreCase));

                    if (swaggerName != null) 
                        schema.Properties[swaggerName].Default = new Microsoft.OpenApi.Any.OpenApiString(defaultAttribute.Value.ToString().ToLower());
                }
            }
        }
    }
}