using System;
using System.Linq;
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
// the TREE (names collide; the TokenId is the authentication lookup key
// that management responses never disclose — see ADR-0006). The TokenId is
// kept in MEMORY only, as the liveness key for the eviction sweep. Logins
// are immutable in the product (User.Name is init-only; the update path
// never touches it), so the grouping segment is stable by construction.
// The per-token subtrees sit under a dedicated "By owner" segment so no
// login can ever collide with the aggregate Authentication failures sensor.
//
// A sealed CLASS, not a record: it holds a lock and mutable sensor fields —
// compiler-generated structural equality would be meaningless here.
public sealed class ApiTokenUsageNode
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    // The path root shared with the aggregate auth-failures sensor. Human-style
    // like the sibling "Clients" node — this is the operator-facing tree.
    public const string TokenUsageRoot = "API tokens";

    // Per-token subtrees live one level below the root, under a dedicated
    // segment: no login — however sanitized — can collide with the aggregate
    // sensor's name at the root level.
    public const string PerTokenSegment = "By owner";

    private const string RestNode = "REST";
    private const string McpNode = "MCP";

    private const string RequestRateNode = "Request rate";
    private const string RequestDurationNode = "Request duration";

    // Sensors idle past the retention window are removed by the server's
    // self-destroy sweep, and history older than the history window is
    // dropped (the DatabaseSensorsStatistics precedent for explicit
    // retention).
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(30);
    private static readonly TimeSpan HistoryPeriod = TimeSpan.FromDays(7);

    private readonly IDataCollector _collector;
    private readonly string _tokenKey;
    private readonly string _prefix;

    // One gate for both channels: each sensor must be created exactly once,
    // while AddValue itself is already thread-safe on the sensors (requests
    // for one token arrive from concurrent connections). The same gate makes
    // Dispose TERMINAL for Add*: after eviction, a racing caller holding the
    // node reference must not resurrect the STOPPED sensors — their
    // accumulated values would never be published, which is silent loss.
    private readonly object _gate = new();

    private bool _disposed;

    // NOT nulled on disposal, deliberately: the sweep's anti-resurrection
    // pass re-stops THESE instances (see StopSensors).
    private IInstantValueSensor<double> _restRate;
    private IBarSensor<double> _restDuration;
    private IInstantValueSensor<double> _mcpRate;
    private IBarSensor<double> _mcpDuration;


    public ApiTokenUsageNode(IDataCollector collector, string ownerLogin, Guid entityId)
    {
        _collector = collector;

        var loginSegment = SanitizeLogin(ownerLogin);
        _tokenKey = $"{loginSegment}/{entityId:D}";
        _prefix = $"{TokenUsageRoot}/{PerTokenSegment}/{_tokenKey}";
    }


    // The log/display key (login/entityId) — carries no TokenId, so logs
    // widen nothing.
    internal string TokenKey => _tokenKey;


    // False = the value was dropped (the node is evicted): the caller logs
    // it once per token (invariant 8 — no silent loss).
    public bool AddRestRequest(double durationMs)
    {
        IInstantValueSensor<double> rate;
        IBarSensor<double> duration;

        lock (_gate)
        {
            if (_disposed)
                return false;

            rate = _restRate ??= CreateRateSensor(RestNode,
                $"REST (/api/v1) requests authenticated by this token ({_tokenKey}).");
            duration = _restDuration ??= CreateDurationSensor(RestNode,
                $"Server-side handling time of one REST (/api/v1) request authenticated by this token ({_tokenKey}).");
        }

        rate.AddValue(1);
        duration.AddValue(durationMs);

        return true;
    }

    public bool AddMcpRequest(double durationMs)
    {
        IInstantValueSensor<double> rate;
        IBarSensor<double> duration;

        lock (_gate)
        {
            if (_disposed)
                return false;

            rate = _mcpRate ??= CreateRateSensor(McpNode,
                $"MCP (/mcp) requests authenticated by this token ({_tokenKey}).");
            duration = _mcpDuration ??= CreateDurationSensor(McpNode,
                $"Server-side handling time of one MCP (/mcp) request authenticated by this token ({_tokenKey}).");
        }

        rate.AddValue(1);
        duration.AddValue(durationMs);

        return true;
    }

    // Terminal for Add* (see _gate), and the FIRST stop of the sensors. The
    // sensor references survive so the sweep can re-stop them later. Named
    // Evict, not Dispose, deliberately: the node is NOT IDisposable — it
    // releases nothing (references survive on purpose), and CLAUDE.md's
    // disposal rule must not be misapplied to it.
    public void Evict()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
        }

        // Stopping runs OUTSIDE _gate: SensorBase.Dispose is sync-over-async
        // and can wait for an in-flight scheduled run — request threads take
        // the same lock and must not be blocked by an eviction.
        StopSensors();
    }

    // Unconditional re-stop of the sensor instances — the anti-resurrection
    // half of retention: the collector never un-registers a disposed sensor,
    // and a collector RESTART (the self-monitoring toggle re-initializes
    // every registered sensor) restarts their send loops. The sensor-level
    // stop is genuinely idempotent, so every sweep re-runs this over the
    // tombstones and kills any resurrection within one tick. Note this is
    // NOT Evict: no _disposed early-return — that guard is what would turn
    // the sweep into a no-op.
    public void StopSensors()
    {
        IInstantValueSensor<double> restRate, mcpRate;
        IBarSensor<double> restDuration, mcpDuration;

        lock (_gate)
        {
            restRate = _restRate;
            restDuration = _restDuration;
            mcpRate = _mcpRate;
            mcpDuration = _mcpDuration;
        }

        StopSensor(restRate);
        StopSensor(restDuration);
        StopSensor(mcpRate);
        StopSensor(mcpDuration);
    }

    // A login is free-form text and becomes a PATH SEGMENT (and reaches log
    // lines and sensor descriptions): everything outside the username
    // charset collapses to '_' — separators would split the segment, control
    // characters would forge log lines (the username validator's regex is
    // unanchored, so a login merely CONTAINING an allowed character passes).
    // Owned HERE, at the type that builds the path: the invariant must hold
    // for every caller of the node.
    internal static string SanitizeLogin(string login)
    {
        if (string.IsNullOrWhiteSpace(login))
            return "_";

        var sanitized = new string(login.Trim().Select(Whitelist).ToArray());

        return sanitized.Length == 0 ? "_" : sanitized;

        static char Whitelist(char c) =>
            char.IsLetterOrDigit(c) || "_.@+-".Contains(c) ? c : '_';
    }

    // The factory interfaces do not carry ISensor, but the concrete sensors
    // implement it — Dispose stops the monitoring send loops. A concrete
    // type ever dropping IDisposable would turn eviction into a silent
    // no-op, so the miss is logged, not just asserted away in Debug builds.
    private static void StopSensor(object sensor)
    {
        // A never-used channel has no sensors — nothing to stop, and the
        // not-IDisposable diagnostic below must stay reserved for the real
        // regression it exists to catch.
        if (sensor is null)
            return;

        if (sensor is not IDisposable disposable)
        {
            Logger.Warn("A token-usage sensor instance is not IDisposable — eviction cannot stop it ({0})", sensor.GetType().Name);
            return;
        }

        disposable.Dispose();
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

    // Durations are BARS (user decision, #1402 follow-up): the collector
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
