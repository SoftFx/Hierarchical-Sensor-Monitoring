using System;
using System.Collections.Concurrent;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;

namespace HSMServer.BackgroundServices;

// The registry of per-token usage nodes plus the aggregate authentication-
// failures counter (#1402) — the ClientStatisticsSensors pattern. Nodes are
// created lazily keyed by the token's UNIQUE EntityId (the login is node-
// internal display state), so the dictionary holds one entry per token EVER
// seen since startup; dead entries leave with the eviction sweep.
internal sealed class ApiTokenUsageSensors : IApiTokenUsageMonitor
{
    private const string AuthFailuresNode = "Authentication failures";

    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly ConcurrentDictionary<Guid, ApiTokenUsageNode> _nodes = new();
    private readonly IDataCollector _collector;

    // The aggregate counter is created eagerly: unlike per-token nodes it
    // belongs to no token, so there is no first-use moment to wait for.
    // It carries no retention options deliberately — permanent by design,
    // unlike the per-token sensors.
    private readonly IInstantValueSensor<double> _authFailures;


    public ApiTokenUsageSensors(IDataCollector collector)
    {
        _collector = collector;

        _authFailures = collector.CreateRateSensor($"{ApiTokenUsageNode.TokenUsageRoot}/{AuthFailuresNode}", new RateSensorOptions
        {
            Alerts = [],
            EnableForGrafana = false,
            Description = "Requests to /api/v1 or /mcp rejected with 401 (missing or invalid credential) — no token to attribute them to.",
        });
    }

    // The registry itself has no off switch: the Enabled gate lives on the
    // adapter the DI composition wraps around this instance (the flag is
    // the live MonitoringOptions value, which the wrapper owns).
    public bool Enabled => true;


    public void AddRestRequest(string tokenId, string ownerLogin, Guid entityId, double durationMs) =>
        NodeFor(tokenId, ownerLogin, entityId).AddRestRequest(durationMs);

    public void AddMcpRequest(string tokenId, string ownerLogin, Guid entityId, double durationMs) =>
        NodeFor(tokenId, ownerLogin, entityId).AddMcpRequest(durationMs);

    public void AddAuthenticationFailure() => _authFailures.AddValue(1);


    // The eviction half of retention (#1403 reviews, rounds 2-3): rate
    // sensors are monitoring sensors posting a 0 every minute forever, so a
    // dead token's subtree never goes IDLE and SelfDestroy alone cannot
    // retire it. The periodic statistics sweep asks IApiTokenManager's
    // IsTokenLive — the sanctioned liveness predicate, covering plain
    // revocation, rotation AND the generation-invalidated window before the
    // stamper runs. A dead token drops and DISPOSES its node: the send
    // loops stop, the sensors go idle, and the server's self-destroy sweep
    // removes them after the retention window. Requests cannot drive this:
    // a dead credential fails authentication before the middleware ever
    // sees a token id. Per-node isolation: one throwing Dispose must not
    // abort the sweep's remaining nodes (CLAUDE.md rule 6).
    public void EvictDeadTokens(Func<string, bool> tokenIsLive)
    {
        foreach (var (key, node) in _nodes)
        {
            if (tokenIsLive(node.TokenId))
                continue;

            try
            {
                if (_nodes.TryRemove(key, out var evicted))
                    evicted.Dispose();
            }
            catch (Exception ex)
            {
                // Per-node isolation (CLAUDE.md rule 6): one throwing
                // Dispose must not abort the sweep's remaining nodes — the
                // entry stays for the next tick to retry.
                Logger.Warn(ex, "API-token usage node eviction failed for token {0}", node.TokenId);
            }
        }
    }


    private ApiTokenUsageNode NodeFor(string tokenId, string ownerLogin, Guid entityId) =>
        _nodes.GetOrAdd(entityId, _ => new ApiTokenUsageNode(_collector, ownerLogin, entityId, tokenId));
}
