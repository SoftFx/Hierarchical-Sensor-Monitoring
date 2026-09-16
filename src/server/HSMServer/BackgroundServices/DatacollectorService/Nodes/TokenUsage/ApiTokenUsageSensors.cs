using System;
using System.Collections.Concurrent;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects.SensorRequests;

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


    private ApiTokenUsageNode NodeFor(string ownerLogin, string entityId) =>
        _nodes.GetOrAdd(KeyOf(ownerLogin, entityId), _ => new ApiTokenUsageNode(_collector, ownerLogin, entityId));

    private static string KeyOf(string ownerLogin, string entityId) => $"{ownerLogin}\n{entityId}";
}
