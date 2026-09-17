using System;
using System.Collections.Concurrent;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;

namespace HSMServer.BackgroundServices;

// The registry of per-token usage nodes plus the aggregate authentication-
// failures counter (#1402) — the ClientStatisticsSensors pattern. Nodes are
// created lazily keyed by the token's UNIQUE EntityId (the login is node-
// internal display state); dead or renamed-away nodes move to the TOMBSTONE
// collection (see EvictDeadTokens) instead of being forgotten.
internal sealed class ApiTokenUsageSensors : IApiTokenUsageMonitor
{
    private const string AuthFailuresNode = "Authentication failures";

    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly ConcurrentDictionary<Guid, ApiTokenUsageNode> _nodes = new();

    // Keyed by the node's PATH KEY (login/entityId), not the bare EntityId:
    // the tombstone blocks re-creating sensors on an OCCUPIED path — a
    // rename's fresh node lives on a DIFFERENT path (the new login) and
    // must not be blocked (#1403 review, round 4).
    private readonly ConcurrentDictionary<string, ApiTokenUsageNode> _tombstones = new(StringComparer.Ordinal);

    private readonly IDataCollector _collector;

    // The aggregate counter is created eagerly: unlike per-token nodes it
    // belongs to no token, so there is no first-use moment to wait for.
    // It carries no retention options deliberately — permanent by design,
    // unlike the per-token sensors. The wording is honest about what a 401
    // on a measured path can be: a bad or missing token credential, or a
    // browser session reaching a token-only endpoint (#1403 review r4).
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


    public void AddRestRequest(string tokenId, string ownerLogin, Guid entityId, double durationMs) =>
        NodeFor(tokenId, ownerLogin, entityId)?.AddRestRequest(durationMs);

    public void AddMcpRequest(string tokenId, string ownerLogin, Guid entityId, double durationMs) =>
        NodeFor(tokenId, ownerLogin, entityId)?.AddMcpRequest(durationMs);

    public void AddAuthenticationFailure() => _authFailures.AddValue(1);


    // The eviction half of retention (#1403 reviews, rounds 2-4): rate
    // sensors are monitoring sensors posting a 0 every minute forever, so a
    // dead token's subtree never goes IDLE and SelfDestroy alone cannot
    // retire it. The periodic statistics sweep asks IApiTokenManager's
    // IsTokenLive — the sanctioned liveness predicate, covering plain
    // revocation, rotation AND the generation-invalidated window before the
    // stamper runs. A dead token moves to the TOMBSTONES and is disposed:
    // the send loops stop, the sensors go idle, and the server's
    // self-destroy sweep removes them after the retention window.
    //
    // Why tombstones rather than forgetting (#1403 review, round 4): the
    // collector never un-registers a disposed sensor, and a collector
    // RESTART (the self-monitoring toggle) re-initializes every registered
    // sensor — resurrecting the dead token's stopped senders. The node is
    // gone from _nodes, so nothing would ever dispose them again: an
    // immortal subtree. Every sweep re-disposes the tombstones (Dispose is
    // idempotent), which kills any resurrection within one tick; and
    // NodeFor refuses tombstoned ids — a fresh node built on the occupied
    // path would get the DEAD sensors back from the storage's path dedup
    // and silently drop every value. The sweep isolates per node, predicate
    // included: one throw skips THIS token's eviction and retries next tick.
    public void EvictDeadTokens(Func<string, bool> tokenIsLive)
    {
        foreach (var (key, node) in _nodes)
        {
            try
            {
                if (tokenIsLive(node.TokenId))
                    continue;

                if (_nodes.TryRemove(key, out var evicted) && _tombstones.TryAdd(evicted.TokenKey, evicted))
                    evicted.Dispose();
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
                tombstone.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "API-token usage tombstone re-disposal failed for {0}", tombstone.TokenKey);
            }
        }
    }


    private ApiTokenUsageNode NodeFor(string tokenId, string ownerLogin, Guid entityId)
    {
        var loginSegment = ApiTokenUsageNode.SanitizeLogin(ownerLogin);

        if (_nodes.TryGetValue(entityId, out var existing))
        {
            // A user rename moves the token to the CURRENT login's segment
            // (#1403 review, round 4): the old node's paths are its history,
            // so the node is tombstoned — exactly like rotation — and a fresh
            // node grows under the new login (a DIFFERENT path, not blocked
            // by the tombstone). The usage tree always groups a token under
            // a login that exists.
            if (existing.OwnerLoginSegment == loginSegment)
                return existing;

            if (_nodes.TryRemove(entityId, out var renamed) && _tombstones.TryAdd(renamed.TokenKey, renamed))
                renamed.Dispose();
        }

        // The tombstone is the OCCUPIED-PATH guard: this exact login/id
        // combination was evicted, and the collector still holds its
        // (disposed) sensors registered under these paths — a fresh node
        // would get them back from the storage's dedup and silently drop
        // every value (#1403 review, rounds 3-4).
        if (_tombstones.ContainsKey(KeyOf(loginSegment, entityId)))
            return null;

        // The arg-based GetOrAdd: the closure-free factory keeps the hit
        // path (where ~100% of traffic lands) allocation-free (#1403 r4).
        return _nodes.GetOrAdd(
            entityId,
            static (id, arg) => new ApiTokenUsageNode(arg.Collector, arg.OwnerLogin, id, arg.TokenId),
            (Collector: _collector, OwnerLogin: ownerLogin, TokenId: tokenId));
    }

    private static string KeyOf(string loginSegment, Guid entityId) => $"{loginSegment}/{entityId:D}";
}
