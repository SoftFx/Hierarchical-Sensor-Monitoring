using System;
using System.Reflection;
using HSMServer.Model.ManagementApi.ChangeSets;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace HSMServer.Filters
{
    /// <summary>
    /// Marks an operation that honours the dry-run header; the swagger filter
    /// documents the header (and its fail-closed rule, #1675) on the operation.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class DryRunHeaderAttribute : Attribute { }


    /// <summary>
    /// The OpenAPI parameter of the dry-run header for the operations marked
    /// with <see cref="DryRunHeaderAttribute"/> — the description is the wire
    /// contract the filter enforces (#1675).
    /// </summary>
    public sealed class DryRunHeaderSwaggerFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            if (context.MethodInfo?.GetCustomAttribute<DryRunHeaderAttribute>() is null)
                return;

            operation.Parameters ??= [];

            operation.Parameters.Add(new OpenApiParameter
            {
                // nameof: the compile-time tie to the header the filter and the
                // write options read — renaming it must not desync the spec.
                Name = ChangeSetHeaders.DryRun,
                In = ParameterLocation.Header,
                Required = false,
                Description = "Preview the write instead of applying it. Strict boolean: \"true\" (any casing) answers 200 with a preview of everything the write would do and applies nothing; \"false\" or an absent/empty header performs the real write; any other value is rejected with 400 validation_failed before anything runs (#1675).",
                Schema = new OpenApiSchema { Type = "string" },
            });
        }
    }
}
