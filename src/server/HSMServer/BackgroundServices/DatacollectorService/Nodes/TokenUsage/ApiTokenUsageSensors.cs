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

    // One "values dropped" trace per dead token, not per dropped request.
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

    // The registry itself has no off switch: the Enabled gate lives on the
    // adapter the DI composition wraps around this instance (the flag is
    // the live MonitoringOptions value, which the wrapper owns).
    public bool Enabled => true;


    public void AddRestRequest(string tokenId, string ownerLogin, Guid entityId, double durationMs)
    {
        var node = NodeFor(tokenId, ownerLogin, entityId);

        if (node is null)
        {
            LogDroppedValues(entityId);
            return;
        }

        node.AddRestRequest(durationMs);
    }

    public void AddMcpRequest(string tokenId, string ownerLogin, Guid entityId, double durationMs)
    {
        var node = NodeFor(tokenId, ownerLogin, entityId);

        if (node is null)
        {
            LogDroppedValues(entityId);
            return;
        }

        node.AddMcpRequest(durationMs);
    }

    public void AddAuthenticationFailure() => _authFailures.AddValue(1);


    // The eviction half of retention: rate sensors are monitoring sensors
    // posting a 0 every minute forever, so a dead token's subtree never goes
    // IDLE and SelfDestroy alone cannot retire it. The periodic sweep asks
    // IApiTokenManager's IsTokenLive — the sanctioned liveness predicate,
    // covering plain revocation, rotation AND the generation-invalidated
    // window. A dead token moves to the TOMBSTONES and its sensors are
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
    // and silently drop every value. Disposal on eviction is UNCONDITIONAL
    // once the node is out of _nodes — the tombstone insert is bookkeeping
    // and must never gate the stop (a lost insert must not strand a live
    // node). Per-node isolation, predicate included: one throw retries next
    // tick. The tombstones live for the process lifetime: the collector
    // never frees the paths, so an expired tombstone would re-open the
    // occupied-path hole, not close a leak (the map is bounded by the
    // tokens ever used and resets on restart).
    public void EvictDeadTokens(Func<string, bool> tokenIsLive)
    {
        foreach (var (key, node) in _nodes)
        {
            try
            {
                if (tokenIsLive(node.TokenId))
                    continue;

                if (_nodes.TryRemove(key, out var evicted))
                {
                    _tombstones.TryAdd(key, evicted);
                    evicted.Dispose();
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


    private ApiTokenUsageNode NodeFor(string tokenId, string ownerLogin, Guid entityId)
    {
        if (_tombstones.ContainsKey(entityId))
            return null;

        // The arg-based GetOrAdd: the closure-free factory keeps the hit
        // path (where ~all traffic lands) allocation-free.
        var node = _nodes.GetOrAdd(
            entityId,
            static (id, arg) => new ApiTokenUsageNode(arg.Collector, arg.OwnerLogin, id, arg.TokenId),
            (Collector: _collector, OwnerLogin: ownerLogin, TokenId: tokenId));

        // The eviction race: the sweep can tombstone this id between the
        // check above and the GetOrAdd — the fresh node landed on the
        // OCCUPIED path and every value through it would be silently
        // dropped by the storage's dedup. Lose the race deliberately:
        // remove the fresh node, stop it, and report the drop (#1403 r5).
        if (_tombstones.ContainsKey(entityId) && _nodes.TryRemove(entityId, out var raced))
        {
            raced.Dispose();
            return null;
        }

        return node;
    }

    // CLAUDE.md invariant 8: dropped values leave a trace — once per dead
    // token, not once per dropped request (a revoked credential fails
    // authentication, so at most in-flight stragglers reach this path).
    private void LogDroppedValues(Guid entityId)
    {
        if (_dropLogOnce.TryAdd(entityId, 0))
            Logger.Warn("Dropping token-usage values for tombstoned token {0}", entityId);
    }
}
