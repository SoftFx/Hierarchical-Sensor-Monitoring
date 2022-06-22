using HSMCommon.Attributes;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Filters
{
    public class SwaggerIgnoreFilter : IDocumentFilter
    {
        private static readonly List<Type> _excludedTypes = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes()).Where(type =>
                type.GetCustomAttributes(true).OfType<SwaggerIgnoreAttribute>().Any()).ToList();

        private static readonly List<string> _manuallyExcludedTypes = new List<string> { "Claim", "ClaimsIdentity" };
        public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
        {
            foreach (var typeToExclude in _excludedTypes)
            {
                swaggerDoc.Components.Schemas.Remove(typeToExclude.Name);
            }

            foreach (var typeToExclude in _manuallyExcludedTypes)
            {
                swaggerDoc.Components.Schemas.Remove(typeToExclude);
            }
        }
    }
}
