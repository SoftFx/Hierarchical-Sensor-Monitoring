using System;
using System.Diagnostics;
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
// Keyed by <owner-login>/<entityId>, NEVER the token name or the TokenId in
// the TREE (names collide and move on rename; the TokenId is the
// authentication lookup key that management responses never disclose — see
// ADR-0006). The TokenId is kept in MEMORY only, as the liveness key for
// the eviction sweep. The Profile token card displays the EntityId, making
// the correlation a glance. The per-token subtrees sit under a dedicated
// "By owner" segment so no login can ever collide with the aggregate
// Authentication failures sensor; sanitization is display-only — two
// logins may share a grouping segment, the EntityIds keep the leaves
// unique (#1403 review, round 2).
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

    // The sanitized login and the ids, cached once at creation: the path is
    // stable and the sweep needs the TokenId for liveness (#1403 review r3).
    private readonly string _ownerLoginSegment;
    private readonly Guid _entityId;
    private readonly string _tokenId;

    private readonly string _prefix;
    private readonly string _tokenKey;

    // One gate for both channels: each sensor must be created exactly once,
    // while AddValue itself is already thread-safe on the sensors (requests
    // for one token arrive from concurrent connections). The same gate makes
    // Dispose TERMINAL: after eviction, a racing caller holding the node
    // reference must not resurrect the STOPPED sensors — their accumulated
    // values would never be published, which is silent data loss.
    private readonly object _gate = new();

    private bool _disposed;

    private IInstantValueSensor<double> _restRate;
    private IBarSensor<double> _restDuration;
    private IInstantValueSensor<double> _mcpRate;
    private IBarSensor<double> _mcpDuration;


    public ApiTokenUsageNode(IDataCollector collector, string ownerLogin, Guid entityId, string tokenId)
    {
        _collector = collector;
        _entityId = entityId;
        _tokenId = tokenId;
        _ownerLoginSegment = SanitizeLogin(ownerLogin);
        _tokenKey = $"{_ownerLoginSegment}/{entityId:D}";
        _prefix = $"{TokenUsageRoot}/{PerTokenSegment}/{_tokenKey}";
    }


    public Guid EntityId => _entityId;

    // The eviction sweep's liveness key — IApiTokenManager.IsTokenLive's
    // argument. Memory only; never rendered into the tree.
    public string TokenId => _tokenId;


    public void AddRestRequest(double durationMs)
    {
        IInstantValueSensor<double> rate;
        IBarSensor<double> duration;

        lock (_gate)
        {
            if (_disposed)
                return;

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
            if (_disposed)
                return;

            rate = _mcpRate ??= CreateRateSensor(McpNode,
                $"MCP (/mcp) requests authenticated by this token ({_tokenKey}).");
            duration = _mcpDuration ??= CreateDurationSensor(McpNode,
                $"Server-side handling time of one MCP (/mcp) request authenticated by this token ({_tokenKey}).");
        }

        rate.AddValue(1);
        duration.AddValue(durationMs);
    }

    // The retention story of a DEAD token (#1403 review, round 2): rate
    // sensors are monitoring sensors — they post a 0 every minute forever and
    // therefore never go idle, so SelfDestroy alone can never retire the
    // subtree. The registry's eviction sweep calls this when the token is no
    // longer live: disposing stops the send loops, the sensors go idle, and
    // the server's self-destroy sweep removes them after the retention
    // window. TERMINAL (#1403 review, round 3): the collector never
    // un-registers a disposed sensor (the path stays occupied and keeps
    // consuming the collector's sensor budget), so re-creating sensors at
    // the same path would hand back the STOPPED instances and silently drop
    // every subsequent value — Add* refuses instead.
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;

            // The factory interfaces do not carry ISensor, but the concrete
            // sensors implement it — Dispose stops the monitoring send loops.
            // The assert catches a concrete type ever dropping IDisposable,
            // which would turn the whole eviction mechanism into a silent no-op.
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

    // A login is free-form text and becomes a PATH SEGMENT: anything that
    // would split or corrupt the segment is collapsed to '_' — the tree
    // must stay one level per intended level no matter what the login
    // contains (#1402 acceptance). Owned HERE, at the type that builds the
    // path: the invariant must hold for every caller of the node, not just
    // today's middleware (#1403 review, round 3). A login collapsing to
    // empty cannot happen through AddUser's validation, but a path like
    // "API tokens//<id>" must never be built either.
    internal static string SanitizeLogin(string login)
    {
        // A missing name must not NRE here either: the contract ("never
        // an empty path segment") is total.
        if (string.IsNullOrWhiteSpace(login))
            return "_";

        var sanitized = string.Join('_', login.Split('/', '\\')).Trim();

        return sanitized.Length == 0 ? "_" : sanitized;
    }

    private static void DisposeSensor(object sensor)
    {
        Debug.Assert(sensor is null or IDisposable, "A concrete sensor type dropped IDisposable — eviction would be a silent no-op");
        (sensor as IDisposable)?.Dispose();
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
