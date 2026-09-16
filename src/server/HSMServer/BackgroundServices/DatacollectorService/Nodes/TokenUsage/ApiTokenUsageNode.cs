using System;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects.SensorRequests;

namespace HSMServer.BackgroundServices;

// The per-token node of the API-token usage monitoring (#1402): the request
// rate and the per-request duration of management-API access authenticated by
// ONE token, split REST (/api/v1) vs MCP (/mcp). Created lazily by
// ApiTokenUsageSensors on the token's first use, and each channel's sensor
// pair on THAT channel's first use — an unused token adds no sensors, and a
// token that never touches /mcp grows no MCP sensors. A revoked or
// rotated-away token's subtree retires on its own: SelfDestroy removes
// sensors idle past the retention window (#1403 review).
//
// Keyed by <owner-login>/<entityId>, NEVER the token name or the TokenId
// (names collide and move on rename; the TokenId is the authentication lookup
// key that management responses never disclose — see ADR-0006). The
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

    // A dead subtree must not outlive its token forever: sensors idle past the
    // retention window are removed by the server's self-destroy sweep, and
    // history older than the history window is dropped (the
    // DatabaseSensorsStatistics precedent for explicit retention).
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(30);
    private static readonly TimeSpan HistoryPeriod = TimeSpan.FromDays(7);

    private readonly IDataCollector _collector;
    private readonly string _prefix;
    private readonly string _tokenKey;

    // One gate for both channels: each sensor must be created exactly once,
    // while AddValue itself is already thread-safe on the sensors (requests
    // for one token arrive from concurrent connections).
    private readonly object _gate = new();

    private IInstantValueSensor<double> _restRate;
    private IInstantValueSensor<double> _restDuration;
    private IInstantValueSensor<double> _mcpRate;
    private IInstantValueSensor<double> _mcpDuration;


    public ApiTokenUsageNode(IDataCollector collector, string ownerLogin, string entityId)
    {
        _collector = collector;
        _tokenKey = $"{ownerLogin}/{entityId}";
        _prefix = $"{TokenUsageRoot}/{_tokenKey}";
    }


    public void AddRestRequest(double durationMs)
    {
        IInstantValueSensor<double> rate;
        IInstantValueSensor<double> duration;

        lock (_gate)
        {
            rate = _restRate ??= CreateRateSensor(RestNode,
                $"REST (/api/v1) requests authenticated by this token ({_tokenKey}).");
            duration = _restDuration ??= CreateDurationSensor(RestNode,
                $"Server-side handling time of one REST (/api/v1) request authenticated by this token ({_tokenKey}).");
        }

        rate.AddValue(1);
        duration.AddValue(durationMs);
    }

    public void AddMcpRequest(double durationMs)
    {
        IInstantValueSensor<double> rate;
        IInstantValueSensor<double> duration;

        lock (_gate)
        {
            rate = _mcpRate ??= CreateRateSensor(McpNode,
                $"MCP (/mcp) requests authenticated by this token ({_tokenKey}).");
            duration = _mcpDuration ??= CreateDurationSensor(McpNode,
                $"Server-side handling time of one MCP (/mcp) request authenticated by this token ({_tokenKey}).");
        }

        rate.AddValue(1);
        duration.AddValue(durationMs);
    }

    private IInstantValueSensor<double> CreateRateSensor(string channelNode, string description) =>
        _collector.CreateRateSensor($"{_prefix}/{channelNode}/{RequestRateNode}", new RateSensorOptions
        {
            Alerts = [],
            EnableForGrafana = false,
            KeepHistory = HistoryPeriod,
            SelfDestroy = RetentionPeriod,
            Description = description,
        });

    private IInstantValueSensor<double> CreateDurationSensor(string channelNode, string description) =>
        _collector.CreateDoubleSensor($"{_prefix}/{channelNode}/{RequestDurationNode}", new InstantSensorOptions
        {
            Alerts = [],
            EnableForGrafana = false,
            SensorUnit = Unit.Milliseconds,
            KeepHistory = HistoryPeriod,
            SelfDestroy = RetentionPeriod,
            Description = description,
        });
}
