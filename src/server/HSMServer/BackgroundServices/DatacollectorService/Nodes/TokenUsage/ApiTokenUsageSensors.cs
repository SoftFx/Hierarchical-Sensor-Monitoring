using System;
using System.Collections.Concurrent;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;

namespace HSMServer.BackgroundServices;

// The registry of per-token usage nodes plus the aggregate authentication-
// failures counter (#1402) — the ClientStatisticsSensors pattern. Nodes are
// created lazily under a composite key (owner login + token EntityId), so the
// dictionary holds one entry per token EVER seen since startup; revoked or
// renamed-away tokens cost one dormant record each until restart.
internal sealed class ApiTokenUsageSensors : IApiTokenUsageMonitor
{
    private const string AuthFailuresNode = "Authentication failures";

    private readonly ConcurrentDictionary<string, ApiTokenUsageNode> _nodes = new(StringComparer.Ordinal);
    private readonly IDataCollector _collector;

    // The aggregate counter is created eagerly: unlike per-token nodes it
    // belongs to no token, so there is no first-use moment to wait for.
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


    public void AddRestRequest(string ownerLogin, string entityId, double durationMs) =>
        NodeFor(ownerLogin, entityId).AddRestRequest(durationMs);

    public void AddMcpRequest(string ownerLogin, string entityId, double durationMs) =>
        NodeFor(ownerLogin, entityId).AddMcpRequest(durationMs);

    public void AddAuthenticationFailure() => _authFailures.AddValue(1);


    // The eviction half of retention (#1403 review, round 2): rate sensors
    // are monitoring sensors posting a 0 every minute forever, so a dead
    // token's subtree never goes IDLE and SelfDestroy alone cannot retire
    // it. The periodic statistics sweep asks whether each node's token
    // record still exists; a gone record (revocation, rotation, owner
    // deletion) drops and DISPOSES the node — the send loops stop, the
    // sensors go idle, and the server's self-destroy sweep removes them
    // after the retention window. Requests cannot drive this: a revoked
    // credential fails authentication before the middleware ever sees a
    // token id, so nothing but the sweep observes the death.
    public void EvictDeadTokens(Func<Guid, Authentication.ApiTokenInfo> tokenByEntityId)
    {
        foreach (var (key, node) in _nodes)
        {
            if (tokenByEntityId(node.EntityId) is not null)
                continue;

            if (_nodes.TryRemove(key, out var evicted))
                evicted.Dispose();
        }
    }


    private ApiTokenUsageNode NodeFor(string ownerLogin, string entityId) =>
        _nodes.GetOrAdd(KeyOf(ownerLogin, entityId), _ => new ApiTokenUsageNode(_collector, ownerLogin, Guid.Parse(entityId)));

    private static string KeyOf(string ownerLogin, string entityId) => $"{ownerLogin}\n{entityId}";
}
