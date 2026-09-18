using System;
using System.Collections.Concurrent;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;

namespace HSMServer.BackgroundServices;

// The registry of per-token usage nodes plus the aggregate authentication-
// failures counter (#1402) — the ClientStatisticsSensors pattern. Nodes are
// created lazily keyed by the token's UNIQUE EntityId; dead nodes move to
// the TOMBSTONE map instead of being forgotten (see EvictDeadTokens).
internal sealed class ApiTokenUsageSensors : IApiTokenUsageMonitor
{
    private const string AuthFailuresNode = "Authentication failures";

    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly ConcurrentDictionary<Guid, ApiTokenUsageNode> _nodes = new();
    private readonly ConcurrentDictionary<Guid, ApiTokenUsageNode> _tombstones = new();

    // One "values dropped" trace per token per eviction epoch (cleared on
    // the collector-restart reset), not per dropped request.
    private readonly ConcurrentDictionary<Guid, byte> _dropLogOnce = new();

    private readonly IDataCollector _collector;

    // The aggregate counter is created eagerly: unlike per-token nodes it
    // belongs to no token, so there is no first-use moment to wait for.
    // It carries no retention options deliberately — permanent by design,
    // unlike the per-token sensors. The wording is honest about what a 401
    // on a measured path can be: a bad or missing token credential, or a
    // browser session reaching a token-only endpoint.
    private readonly IInstantValueSensor<double> _authFailures;


    public ApiTokenUsageSensors(IDataCollector collector)
    {
        _collector = collector;

        _authFailures = collector.CreateRateSensor($"{ApiTokenUsageNode.TokenUsageRoot}/{AuthFailuresNode}", new RateSensorOptions
        {
            Alerts = [],
            EnableForGrafana = false,
            Description = "Requests to /api/v1 or /mcp rejected with 401 — a missing or invalid token credential (or a browser session on a token-only endpoint); no token to attribute them to.",
        });
    }

    // No Enabled here, deliberately: the gate lives on the DI adapter
    // (MonitoringGate, the live MonitoringOptions value) — the raw registry
    // is always on, so it implements only the add-surface. The gate half of
    // the interface (IApiTokenUsageGate) routes every Enabled reader to the
    // adapter at compile time.


    public void AddRestRequest(string ownerLogin, Guid entityId, double durationMs)
    {
        var node = NodeFor(ownerLogin, entityId);

        if (node?.AddRestRequest(durationMs) != true)
            LogDroppedValues(entityId);
    }

    public void AddMcpRequest(string ownerLogin, Guid entityId, double durationMs)
    {
        var node = NodeFor(ownerLogin, entityId);

        if (node?.AddMcpRequest(durationMs) != true)
            LogDroppedValues(entityId);
    }

    public void AddAuthenticationFailure() => _authFailures.AddValue(1);


    // The eviction half of retention: rate sensors are monitoring sensors
    // posting a 0 every minute forever, so a dead token's subtree never goes
    // IDLE and SelfDestroy alone cannot retire it. The periodic sweep asks
    // the COMPOSED liveness predicate (TokenUsageLiveness: record live AND
    // owner still exists — IsTokenLive alone never consults the owner, and
    // owner deletion invalidates the credential without touching the token
    // row). A dead token moves to the TOMBSTONES and its sensors are
    // stopped; the server's self-destroy sweep then retires the idle
    // sensors after the retention window.
    //
    // The tombstones exist because the collector never un-registers a
    // disposed sensor, and a collector RESTART (the self-monitoring toggle)
    // re-initializes every registered one — resurrecting the stopped send
    // loops. Every sweep re-stops the tombstoned instances (the sensor-level
    // stop is idempotent), killing any resurrection within one tick, and
    // NodeFor refuses a tombstoned id: a fresh node built on the occupied
    // path would get the DEAD sensors back from the storage's path dedup
    // and silently drop every value.
    //
    // ORDERING: the tombstone claim goes in BEFORE the node leaves _nodes.
    // Remove-first would leave a window where the id is in NEITHER map, and
    // a concurrent NodeFor's GetOrAdd landing there returns a fresh node
    // (its post-check sees no tombstone yet) that then sits in _nodes for a
    // tombstoned id — poisoned by the storage's path dedup, silently
    // dropping every value. Claim-first makes NodeFor's post-check always
    // observe the tombstone and lose the race deliberately. Disposal on
    // eviction stays UNCONDITIONAL once the node is out of _nodes — the
    // claim (which can lose only to an already-present tombstone) is still
    // bookkeeping and never gates the stop. Per-node isolation, predicate
    // included: one throw retries next tick. The tombstones live for the
    // process lifetime: the collector never frees the paths, so an expired
    // tombstone would re-open the occupied-path hole, not close a leak
    // (the map is bounded by the tokens ever used and resets on restart).
    public void EvictDeadTokens(Func<Guid, bool> tokenIsLive)
    {
        foreach (var (key, node) in _nodes)
        {
            try
            {
                if (tokenIsLive(key))
                    continue;

                // Claim the id FIRST — see ORDERING above.
                _tombstones.TryAdd(key, node);

                if (_nodes.TryRemove(key, out var evicted))
                {
                    evicted.Evict();
                    // Tombstoning is irreversible for the process lifetime —
                    // the step is auditable at Info, not just the Warn on failure.
                    Logger.Info("API-token usage subtree evicted (token dead): {0}", evicted.TokenKey);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "API-token usage node eviction failed for {0}", node.TokenKey);
            }
        }

        foreach (var tombstone in _tombstones.Values)
        {
            try
            {
                tombstone.StopSensors();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "API-token usage tombstone re-stop failed for {0}", tombstone.TokenKey);
            }
        }
    }


    private ApiTokenUsageNode NodeFor(string ownerLogin, Guid entityId)
    {
        if (_tombstones.ContainsKey(entityId))
            return null;

        // The arg-based GetOrAdd: the closure-free factory keeps the hit
        // path (where ~all traffic lands) allocation-free.
        var node = _nodes.GetOrAdd(
            entityId,
            static (id, arg) => new ApiTokenUsageNode(arg.Collector, arg.OwnerLogin, id),
            (Collector: _collector, OwnerLogin: ownerLogin));

        // The eviction race: the sweep can tombstone this id between the
        // check above and the GetOrAdd — the fresh node landed on the
        // OCCUPIED path and every value through it would be silently
        // dropped by the storage's dedup. Lose the race deliberately:
        // remove the fresh node, stop it, and report the drop (#1403
        // review). The sweep's claim-first ordering guarantees this
        // post-check always observes a tombstone claimed before the node
        // left _nodes.
        if (_tombstones.ContainsKey(entityId) && _nodes.TryRemove(entityId, out var raced))
        {
            raced.Evict();
            return null;
        }

        return node;
    }

    // A collector restart invalidates every cached sensor instance: one
    // registered during the stopping phase is returned INERT and would be
    // cached forever by the node's ??= (permanent silent loss for that
    // token/channel). Clearing the LIVE nodes lets each rebuild on its next
    // request; the tombstones stay — the occupied paths survive the restart.
    // The drop-log memory is cleared with them: a request racing the restart
    // can observe the (now evicted) node and plant a drop trace the restart
    // made moot — leaving it would permanently mute the once-per-token warn
    // for the NEXT genuine drop (#1403 review).
    public void ResetLiveNodes()
    {
        foreach (var (key, _) in _nodes)
            if (_nodes.TryRemove(key, out var removed))
            {
                removed.Evict();
                _dropLogOnce.TryRemove(key, out _);
            }
    }

    // CLAUDE.md invariant 8: dropped values leave a trace — once per token
    // per eviction epoch, not once per dropped request. The wording covers
    // both causes of a refused value: a dead token's straggler (a revoked
    // credential fails authentication, so at most in-flight requests reach
    // this path) and an in-flight request holding a node a collector
    // restart just reset — the token is not dead in that case (#1403
    // review).
    private void LogDroppedValues(Guid entityId)
    {
        if (_dropLogOnce.TryAdd(entityId, 0))
            Logger.Warn("Dropping token-usage values for token {0} (node evicted: dead token or collector restart)", entityId);
    }
}
