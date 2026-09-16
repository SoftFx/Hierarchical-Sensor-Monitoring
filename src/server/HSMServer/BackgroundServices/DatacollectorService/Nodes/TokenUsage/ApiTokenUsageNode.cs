using System;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects.SensorRequests;

namespace HSMServer.BackgroundServices;

// The per-token node of the API-token usage monitoring (#1402): the request
// rate and the bar-aggregated duration of management-API access
// authenticated by ONE token, split REST (/api/v1) vs MCP (/mcp). Created
// lazily by ApiTokenUsageSensors on the token's first use, and each
// channel's sensor pair on THAT channel's first use — an unused token adds
// no sensors, and a token that never touches /mcp grows no MCP sensors.
//
// Keyed by <owner-login>/<entityId>, NEVER the token name or the TokenId
// (names collide and move on rename; the TokenId is the authentication
// lookup key that management responses never disclose — see ADR-0006). The
// Profile token card displays the EntityId, making the correlation a glance.
// The per-token subtrees sit under a dedicated "By owner" segment so no
// login can ever collide with the aggregate Authentication failures sensor;
// sanitization is display-only — two logins may share a grouping segment,
// the EntityIds keep the leaves unique (#1403 review, round 2).
//
// A sealed CLASS, not a record: it holds a lock and mutable sensor fields —
// compiler-generated structural equality would be meaningless here.
public sealed class ApiTokenUsageNode : IDisposable
{
    // The path root shared with the aggregate auth-failures sensor. Human-style
    // like the sibling "Clients" node — this is the operator-facing tree.
    public const string TokenUsageRoot = "API tokens";

    // Per-token subtrees live one level below the root, under a dedicated
    // segment: no login — however sanitized — can collide with the aggregate
    // sensor's name at the root level (#1403 review, round 2).
    public const string PerTokenSegment = "By owner";

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

    // For the eviction sweep (#1403 review, round 2): identifies which token
    // record this subtree belongs to, so a revoked/rotated-away token's node
    // can be found and disposed when the record is gone.
    private readonly Guid _entityId;

    // One gate for both channels: each sensor must be created exactly once,
    // while AddValue itself is already thread-safe on the sensors (requests
    // for one token arrive from concurrent connections).
    private readonly object _gate = new();

    private IInstantValueSensor<double> _restRate;
    private IBarSensor<double> _restDuration;
    private IInstantValueSensor<double> _mcpRate;
    private IBarSensor<double> _mcpDuration;


    public ApiTokenUsageNode(IDataCollector collector, string ownerLogin, Guid entityId)
    {
        _collector = collector;
        _entityId = entityId;
        _tokenKey = $"{ownerLogin}/{entityId:D}";
        _prefix = $"{TokenUsageRoot}/{PerTokenSegment}/{_tokenKey}";
    }


    public Guid EntityId => _entityId;


    public void AddRestRequest(double durationMs)
    {
        IInstantValueSensor<double> rate;
        IBarSensor<double> duration;

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
        IBarSensor<double> duration;

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

    // The retention story of a REVOKED token (#1403 review, round 2): rate
    // sensors are monitoring sensors — they post a 0 every minute forever and
    // therefore never go idle, so SelfDestroy alone can never retire the
    // subtree. The registry's eviction sweep calls this when the token record
    // is gone: disposing stops the send loops, the sensors go idle, and the
    // server's self-destroy sweep removes them after the retention window.
    public void Dispose()
    {
        lock (_gate)
        {
            // The factory interfaces do not carry ISensor, but the concrete
            // sensors implement it — Dispose stops the monitoring send loops.
            DisposeSensor(_restRate);
            DisposeSensor(_restDuration);
            DisposeSensor(_mcpRate);
            DisposeSensor(_mcpDuration);

            _restRate = null;
            _restDuration = null;
            _mcpRate = null;
            _mcpDuration = null;
        }
    }

    private static void DisposeSensor(object sensor) =>
        (sensor as IDisposable)?.Dispose();

    private IInstantValueSensor<double> CreateRateSensor(string channelNode, string description) =>
        _collector.CreateRateSensor($"{_prefix}/{channelNode}/{RequestRateNode}", new RateSensorOptions
        {
            Alerts = [],
            EnableForGrafana = false,
            KeepHistory = HistoryPeriod,
            SelfDestroy = RetentionPeriod,
            Description = description,
        });

    // Durations are BARS (#1402 follow-up, user decision): the collector
    // aggregates min/max/mean/count per bar period — one stored point per
    // window however hot the token, and slow requests stay visible as the
    // bar's max. The trade-off: exact per-request tails (percentile slicing
    // over raw samples) are gone — find a single slow request by traceId in
    // the logs once the token is identified.
    private IBarSensor<double> CreateDurationSensor(string channelNode, string description) =>
        _collector.CreateDoubleBarSensor($"{_prefix}/{channelNode}/{RequestDurationNode}", new BarSensorOptions
        {
            Alerts = [],
            EnableForGrafana = false,
            SensorUnit = Unit.Milliseconds,
            KeepHistory = HistoryPeriod,
            SelfDestroy = RetentionPeriod,
            Description = description,
        });
}
