using System;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects.SensorRequests;

namespace HSMServer.BackgroundServices;

// The per-token node of the API-token usage monitoring (#1402): the request
// rate and the per-request duration of management-API access authenticated by
// ONE token, split REST (/api/v1) vs MCP (/mcp). Created lazily by
// ApiTokenUsageSensors on the token's first use — an unused token adds no
// sensors; a revoked token's subtree simply goes silent and TTL cleans it.
//
// Keyed by <owner-login>/<entityId>, NEVER the token name or the TokenId
// (names collide and move on rename; the TokenId is the authentication lookup
// key that management responses never disclose — see docs/adr/0001). The
// Profile token card displays the EntityId, making the correlation a glance.
public sealed record ApiTokenUsageNode
{
    // The path root shared with the aggregate auth-failures sensor. Human-style
    // like the sibling "Clients" node — this is the operator-facing tree.
    public const string TokenUsageRoot = "API tokens";

    private const string RestNode = "REST";
    private const string McpNode = "MCP";

    private const string RequestRateNode = "Request rate";
    private const string RequestDurationNode = "Request duration";

    private readonly IInstantValueSensor<double> _restRate;
    private readonly IInstantValueSensor<double> _restDuration;
    private readonly IInstantValueSensor<double> _mcpRate;
    private readonly IInstantValueSensor<double> _mcpDuration;


    public ApiTokenUsageNode(IDataCollector collector, string ownerLogin, string entityId)
    {
        var prefix = $"{TokenUsageRoot}/{ownerLogin}/{entityId}";

        _restRate = collector.CreateRateSensor($"{prefix}/{RestNode}/{RequestRateNode}", new RateSensorOptions
        {
            Alerts = [],
            EnableForGrafana = false,
            Description = $"REST (/api/v1) requests authenticated by this token ({ownerLogin}/{entityId}).",
        });

        _restDuration = collector.CreateDoubleSensor($"{prefix}/{RestNode}/{RequestDurationNode}", new InstantSensorOptions
        {
            SensorUnit = Unit.Milliseconds,
            Description = $"Server-side handling time of one REST (/api/v1) request authenticated by this token ({ownerLogin}/{entityId}).",
        });

        _mcpRate = collector.CreateRateSensor($"{prefix}/{McpNode}/{RequestRateNode}", new RateSensorOptions
        {
            Alerts = [],
            EnableForGrafana = false,
            Description = $"MCP (/mcp) requests authenticated by this token ({ownerLogin}/{entityId}).",
        });

        _mcpDuration = collector.CreateDoubleSensor($"{prefix}/{McpNode}/{RequestDurationNode}", new InstantSensorOptions
        {
            SensorUnit = Unit.Milliseconds,
            Description = $"Server-side handling time of one MCP (/mcp) request authenticated by this token ({ownerLogin}/{entityId}).",
        });
    }


    public void AddRestRequest(double durationMs)
    {
        _restRate.AddValue(1);
        _restDuration.AddValue(durationMs);
    }

    public void AddMcpRequest(double durationMs)
    {
        _mcpRate.AddValue(1);
        _mcpDuration.AddValue(durationMs);
    }
}
