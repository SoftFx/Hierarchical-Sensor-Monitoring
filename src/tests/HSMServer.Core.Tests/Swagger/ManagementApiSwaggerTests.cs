using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HSMServer.Authentication;
using HSMServer.Controllers;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;
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

        private static readonly MethodInfo GrafanaAction = typeof(HSMServer.Controllers.GrafanaDatasources.JsonSource.JsonDatasourceController)
            .GetMethod(nameof(HSMServer.Controllers.GrafanaDatasources.JsonSource.JsonDatasourceController.ReadHistory));

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
        public void KeyHeaderFilter_GrafanaActions_KeepTheKeyHeader()
        {
            // The Grafana JSON datasource authenticates EXCLUSIVELY through the Key
            // header (TryGetKey reads Request.Headers["Key"]), and its request types do
            // not derive from BaseRequest — a positive BaseRequest-type match would
            // silently strip its credential from the spec (review finding on #1366).
            var operation = new Microsoft.OpenApi.Models.OpenApiOperation();

            new DataRequestHeaderSwaggerFilter().Apply(operation, Context(GrafanaAction));

            Assert.Contains(operation.Parameters, p => p.Name == "Key" && p.In == Microsoft.OpenApi.Models.ParameterLocation.Header);
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
        // aicontext/features/server/management-api/feature.md — 500 on every action:
        // the /api exception handler can answer any of them. An action absent from
        // this map fails the conventions test — adding a management endpoint means
        // documenting it here (and in the feature doc) in the same change.
        private static readonly Dictionary<(Type Controller, string Action), int[]> RequiredErrorResponses = new()
        {
            [(typeof(AlertTemplatesApiController), nameof(AlertTemplatesApiController.GetTemplates))] = [400, 401, 500],
            [(typeof(AlertTemplatesApiController), nameof(AlertTemplatesApiController.GetTemplate))] = [401, 403, 404, 500],
            [(typeof(AlertTemplatesApiController), nameof(AlertTemplatesApiController.CreateTemplate))] = [400, 401, 403, 404, 409, 500],
            [(typeof(AlertTemplatesApiController), nameof(AlertTemplatesApiController.UpdateTemplate))] = [400, 401, 403, 404, 409, 500],
            [(typeof(AlertTemplatesApiController), nameof(AlertTemplatesApiController.DeleteTemplate))] = [401, 403, 404, 409, 500],
            [(typeof(AlertSchedulesApiController), nameof(AlertSchedulesApiController.GetSchedules))] = [400, 401, 403, 500],
            [(typeof(AlertSchedulesApiController), nameof(AlertSchedulesApiController.GetSchedule))] = [401, 403, 404, 500],
        };


        [Fact]
        public void ManagementActions_DeclareTheDocumentedResponses()
        {
            // inherit: true — the runtime guard admits endpoints by ENDPOINT metadata,
            // which includes attributes inherited from a base controller; the
            // conventions set must not silently diverge from it.
            var controllers = typeof(AlertTemplatesApiController).Assembly.GetTypes()
                .Where(t => t.IsDefined(typeof(ManagementApiAttribute), inherit: true))
                .ToList();

            var documented = new HashSet<string>();

            foreach (var controller in controllers)
                // No DeclaredOnly: HTTP actions inherited from a base management
                // controller must be documented like any other — the same inherit:true
                // view the discovery above and the runtime guard use.
                foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                             .Where(IsHttpAction))
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

        // Endpoints only: public helpers and property accessors are not swagger
        // operations and must not trip the conventions map.
        private static bool IsHttpAction(MethodInfo method) =>
            method.GetCustomAttributes().OfType<HttpMethodAttribute>().Any();


        // The enum value tables published in the DTO XML remarks are the agent-facing
        // source of truth (the fields stay bytes so their validation runs AFTER
        // authorization — see feature.md). This suite parses the remarks out of the
        // source and pins them against the domain enums the controller validates with:
        // a renamed/added/removed member — or a wrong table, like the sensorStatus
        // inversion this test was written after (review round 2 on #1366) — fails here
        // instead of corrupting agent payloads silently.
        [Fact]
        public void DocumentedEnumTables_MatchTheDomainEnums()
        {
            var source = ReadRepoFile("src/server/HSMServer/Model/ManagementApi/AlertTemplates/AlertTemplateDto.cs");

            AssertTable(source, "public byte SensorType { get; init; }",
                typeof(HSMCommon.Model.SensorType),
                extras: [(100L, nameof(HSMServer.Core.Model.AlertTemplateModel.AnyType))]);

            AssertTable(source, "public byte SensorStatus { get; init; }",
                typeof(HSMCommon.Model.SensorStatus));

            AssertTable(source, "public byte Combination { get; init; }", typeof(PolicyCombination));
            AssertTable(source, "public byte Operation { get; init; }", typeof(PolicyOperation));
            AssertTable(source, "public byte Property { get; init; }", typeof(PolicyProperty));
            AssertTable(source, "public byte Type { get; init; }", typeof(TargetType));
            AssertTable(source, "public byte RepeateMode { get; init; }",
                typeof(HSMServer.Core.Model.Policies.AlertRepeatMode));

            // The sparse TimeInterval table lives in the RECORD summary (it documents
            // the whole shape: which intervals are ticks-authoritative).
            AssertTable(source, "public sealed record TimeIntervalDto", typeof(HSMServer.Core.Model.TimeInterval));
        }

        private static void AssertTable(string source, string memberDeclaration, Type enumType,
            params (long Value, string Name)[] extras)
        {
            var index = source.IndexOf(memberDeclaration, StringComparison.Ordinal);
            Assert.True(index >= 0, $"{memberDeclaration} not found in the DTO source");

            var before = source[..index];
            var summaryEnd = before.LastIndexOf("</summary>", StringComparison.Ordinal);
            Assert.True(summaryEnd >= 0, $"no <summary> found above {memberDeclaration}");

            var summaryStart = before.LastIndexOf("<summary>", summaryEnd, StringComparison.Ordinal);
            var summary = before[summaryStart..summaryEnd];

            var documented = System.Text.RegularExpressions.Regex.Matches(summary, @"(-?\d+)=([A-Za-z]+)")
                .Select(m => (Value: long.Parse(m.Groups[1].Value), Name: m.Groups[2].Value))
                .ToDictionary(pair => pair.Value, pair => pair.Name);

            var actual = Enum.GetValues(enumType)
                .Cast<object>()
                .Select(value => (Value: Convert.ToInt64(value), Name: value.ToString()))
                .Concat(extras)
                .ToDictionary(pair => pair.Value, pair => pair.Name);

            Assert.Equal(actual, documented);
        }

        private static string ReadRepoFile(string relativeSource)
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
                directory = directory.Parent;

            var source = directory is null
                ? null
                : Path.Combine(directory.FullName, relativeSource.Replace('/', Path.DirectorySeparatorChar));

            Assert.True(source is not null && File.Exists(source),
                $"Cannot locate {relativeSource} (resolved: {source ?? "<none>"}, started from {AppContext.BaseDirectory})");

            return File.ReadAllText(source);
        }
    }
}
