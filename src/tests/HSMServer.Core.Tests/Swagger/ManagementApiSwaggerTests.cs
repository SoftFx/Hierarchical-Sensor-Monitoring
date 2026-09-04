using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HSMServer.Authentication;
using HSMServer.Controllers;
using HSMServer.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Swashbuckle.AspNetCore.SwaggerGen;
using Xunit;

namespace HSMServer.Core.Tests.Swagger
{
    // OpenAPI publication of the management API (#1353, epic #1347): the single
    // swagger doc the server serves also carries the sensor-data API, so the
    // management-specific machinery must be SCOPED — the Key/ClientName headers belong
    // to sensor-data actions only, and the bearer security requirement to management
    // actions only. The conventions test pins that every management action documents
    // the error responses of the uniform contract, so an agent working from the spec
    // alone knows every outcome before calling.
    public class ManagementApiSwaggerTests
    {
        private static readonly MethodInfo SensorDataAction = typeof(SensorsController)
            .GetMethods()
            .Single(m => m.Name == nameof(SensorsController.Post) &&
                         m.GetParameters().Single().ParameterType.Name == "IntSensorValue");

        private static readonly MethodInfo ManagementAction =
            typeof(AlertTemplatesApiController).GetMethod(nameof(AlertTemplatesApiController.GetTemplates));


        private static OperationFilterContext Context(MethodInfo action) =>
            new(new Microsoft.AspNetCore.Mvc.ApiExplorer.ApiDescription
                {
                    ActionDescriptor = new ControllerActionDescriptor { MethodInfo = action },
                },
                null,
                new SchemaRepository(),
                action);


        [Fact]
        public void KeyHeaderFilter_SensorDataActions_KeepTheKeyHeader()
        {
            var operation = new Microsoft.OpenApi.Models.OpenApiOperation();

            new DataRequestHeaderSwaggerFilter().Apply(operation, Context(SensorDataAction));

            Assert.Contains(operation.Parameters, p => p.Name == "Key" && p.In == Microsoft.OpenApi.Models.ParameterLocation.Header);
            Assert.Contains(operation.Parameters, p => p.Name == "ClientName");
        }

        [Fact]
        public void KeyHeaderFilter_ManagementActions_AdvertiseNoKeyHeader()
        {
            // The management API authenticates with the Authorization bearer header;
            // advertising a Key header there would mislead every spec-driven agent.
            var operation = new Microsoft.OpenApi.Models.OpenApiOperation();

            new DataRequestHeaderSwaggerFilter().Apply(operation, Context(ManagementAction));

            Assert.DoesNotContain(operation.Parameters, p => p.Name == "Key");
            Assert.DoesNotContain(operation.Parameters, p => p.Name == "ClientName");
        }

        [Fact]
        public void SecurityFilter_ManagementActions_CarryTheBearerRequirement()
        {
            var operation = new Microsoft.OpenApi.Models.OpenApiOperation();

            new ManagementApiSecuritySwaggerFilter().Apply(operation, Context(ManagementAction));

            var requirement = Assert.Single(operation.Security);
            Assert.Contains(requirement.Keys, scheme =>
                scheme.Reference.Id == ManagementApiSecuritySwaggerFilter.SchemeName &&
                scheme.Reference.Type == Microsoft.OpenApi.Models.ReferenceType.SecurityScheme);
        }

        [Fact]
        public void SecurityFilter_OtherActions_GetNoBearerRequirement()
        {
            var operation = new Microsoft.OpenApi.Models.OpenApiOperation();

            new ManagementApiSecuritySwaggerFilter().Apply(operation, Context(SensorDataAction));

            Assert.Empty(operation.Security);
        }


        // The per-action error-response sets of the area, from
        // aicontext/features/server/management-api/feature.md. An action absent from
        // this map fails the conventions test — adding a management endpoint means
        // documenting it here (and in the feature doc) in the same change.
        private static readonly Dictionary<(Type Controller, string Action), int[]> RequiredErrorResponses = new()
        {
            [(typeof(AlertTemplatesApiController), nameof(AlertTemplatesApiController.GetTemplates))] = [400, 401],
            [(typeof(AlertTemplatesApiController), nameof(AlertTemplatesApiController.GetTemplate))] = [401, 403, 404],
            [(typeof(AlertTemplatesApiController), nameof(AlertTemplatesApiController.CreateTemplate))] = [400, 401, 403, 404, 409],
            [(typeof(AlertTemplatesApiController), nameof(AlertTemplatesApiController.UpdateTemplate))] = [400, 401, 403, 404, 409],
            [(typeof(AlertTemplatesApiController), nameof(AlertTemplatesApiController.DeleteTemplate))] = [401, 403, 404, 409],
            [(typeof(AlertSchedulesApiController), nameof(AlertSchedulesApiController.GetSchedules))] = [400, 401, 403],
            [(typeof(AlertSchedulesApiController), nameof(AlertSchedulesApiController.GetSchedule))] = [401, 403, 404],
        };


        [Fact]
        public void ManagementActions_DeclareTheDocumentedResponses()
        {
            var controllers = typeof(AlertTemplatesApiController).Assembly.GetTypes()
                .Where(t => t.IsDefined(typeof(ManagementApiAttribute), inherit: false))
                .ToList();

            var documented = new HashSet<string>();

            foreach (var controller in controllers)
                foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    documented.Add($"{controller.Name}.{action.Name}");

                    var declared = action.GetCustomAttributes<ProducesResponseTypeAttribute>()
                        .Select(a => a.StatusCode)
                        .ToList();

                    Assert.True(RequiredErrorResponses.TryGetValue((controller, action.Name), out var required),
                        $"{controller.Name}.{action.Name} is not in the swagger conventions map — document its responses and add it to RequiredErrorResponses");

                    foreach (var status in required)
                        Assert.Contains(status, declared);

                    // A success (2xx) response is documented for every action.
                    Assert.Contains(declared, status => status is >= 200 and < 300);
                }

            // No dead entries: everything in the map belongs to a real management action.
            foreach (var (controller, action) in RequiredErrorResponses.Keys)
                Assert.Contains($"{controller.Name}.{action}", documented);
        }
    }
}
