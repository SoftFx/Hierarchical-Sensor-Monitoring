using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using HSMServer.Mcp;
using HSMServer.Model.ManagementApi.SensorTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using Xunit;

namespace HSMServer.Core.Tests.Mcp
{
    // The MCP wiring itself (#1391): the registration must surface EXACTLY the
    // nine read-only tools the spec names (schemas built by the SDK — a bad tool
    // signature throws at registration, so this also proves the signatures are
    // constructible), the server identity, and the camelCase wire casing shared
    // with REST. The endpoint-level authorization (MapMcp + RequireAuthorization)
    // is routing configuration and stays a documented residual — see
    // aicontext/features/server/mcp/tests.md.
    public class HsmMcpServerRegistrationTests
    {
        [Fact]
        public void AddHsmMcpServer_RegistersExactlyTheNineSpecTools()
        {
            using var provider = new ServiceCollection().AddHsmMcpServer().BuildServiceProvider();

            var names = provider.GetRequiredService<IEnumerable<McpServerTool>>()
                .Select(tool => tool.ProtocolTool.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            Assert.Equal(
            [
                "find_sensors",
                "get_alert_schedule",
                "get_alert_template",
                "get_node",
                "get_sensor",
                "get_sensor_history",
                "list_alert_schedules",
                "list_alert_templates",
                "list_products",
            ], names);
        }

        [Fact]
        public void AddHsmMcpServer_IdentifiesTheServer()
        {
            using var provider = new ServiceCollection().AddHsmMcpServer().BuildServiceProvider();

            var serverInfo = provider.GetRequiredService<IOptions<ModelContextProtocol.Server.McpServerOptions>>()
                .Value.ServerInfo;

            Assert.Equal("HSMServer", serverInfo.Name);
            Assert.False(string.IsNullOrEmpty(serverInfo.Version));
        }

        [Fact]
        public void AddHsmMcpServer_PinsStatelessHttpTransport()
        {
            // The tools resolve the caller through the ambient HTTP context
            // (McpToolContext), which holds only while a tools/call executes
            // INLINE within its POST — the stateless mode. The SDK 2.2.0
            // default is stateless (since the 2026-07-28 protocol revision),
            // but an SDK default change would otherwise silently degrade every
            // tool call to the backstop error; pinned here like the camelCase
            // pin below, so SDK drift fails a test instead.
            using var provider = new ServiceCollection().AddHsmMcpServer().BuildServiceProvider();

            var transport = provider.GetRequiredService<IOptions<HttpServerTransportOptions>>().Value;

            Assert.True(transport.Stateless);
        }

        [Fact]
        public void ToolResults_RenderCamelCase_UnderTheSdkDefaultOptions()
        {
            // The SDK's documented default enables JsonSerializerDefaults.Web;
            // this pins that the tool result records — and hence the shared REST
            // DTOs — render camelCase under it, keeping the two transports'
            // JSON keys identical (an SDK default change fails here instead of
            // silently diverging the surfaces).
            var summary = JsonSerializer.Serialize(new McpSensorSummary
            {
                Id = Guid.NewGuid(),
                Path = "alpha/eth0",
                Type = "Double",
                Status = "Ok",
                Unit = "%",
            }, McpJsonUtilities.DefaultOptions);

            Assert.Contains("\"id\"", summary);
            Assert.Contains("\"path\"", summary);
            Assert.DoesNotContain("\"Path\"", summary);

            var envelope = JsonSerializer.Serialize(new McpSensorsResult
            {
                Sensors = [],
                TotalFound = 3,
            }, McpJsonUtilities.DefaultOptions);

            Assert.Contains("\"sensors\"", envelope);
            Assert.Contains("\"totalFound\"", envelope);
            Assert.DoesNotContain("\"TotalFound\"", envelope);
        }

        [Fact]
        public void ItemDtos_RenderTheSamePropertyKeys_OnMcpAndMvcJsonOptions()
        {
            // The parity claim needs pinning beyond CASING: DefaultIgnoreCondition
            // decides whether a null lastValue/unit/status/parent appears as null
            // or is OMITTED. Writing this pin (#1392 review, round 5) exposed the
            // actual contract: the SDK's McpJsonUtilities.DefaultOptions set
            // WhenWritingNull, MVC's Web defaults write explicit nulls — so an
            // item DTO's null members are ABSENT on MCP and PRESENT-as-null on
            // REST (absent ≡ null for any JSON consumer; the two options cannot
            // be aligned without mutating the SDK-owned options the protocol
            // envelope itself serializes through, or changing the shipped REST
            // wire). Pin both key sets EXACTLY: an SDK default change, a DTO
            // edit, or an MVC options change that shifts either shape fails
            // here instead of silently diverging the surfaces.
            var mvcOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals,
            };

            var sensor = new SensorDto { Id = Guid.NewGuid(), Path = "alpha/eth0" };

            var mcpKeys = KeysOf(JsonSerializer.Serialize(sensor, McpJsonUtilities.DefaultOptions));
            var mvcKeys = KeysOf(JsonSerializer.Serialize(sensor, mvcOptions));

            // MCP: only the non-null members travel (WhenWritingNull).
            Assert.Equal(["creationDate", "id", "path"], [.. mcpKeys.Order()]);

            // REST: the full shape, null members included.
            Assert.Equal(
            [
                "creationDate", "description", "enumOptions", "id", "lastUpdate",
                "lastValue", "name", "parent", "path", "product", "state", "status", "type", "unit",
            ], [.. mvcKeys.Order()]);
        }

        private static List<string> KeysOf(string json)
        {
            using var document = JsonDocument.Parse(json);

            return [.. document.RootElement.EnumerateObject().Select(property => property.Name)];
        }
    }
}
