using System;
using HSMServer.ServerConfiguration;
using Microsoft.Extensions.Options;

namespace HSMServer.BackgroundServices;

// The DI composition's adapter over the token-usage registry (#1403 review,
// round 3): the middleware's Enabled gate reads the LIVE MonitoringOptions
// value — with self-monitoring off, the collector never publishes and the
// statistics sweep never runs, so measurement would only burn per-request
// lookups and register sensors into a dead pipeline.
internal sealed class MonitoringGate(IOptionsMonitor<MonitoringOptions> options, IApiTokenUsageMonitor inner)
    : IApiTokenUsageMonitor
{
    public bool Enabled => options.CurrentValue.IsMonitoringEnabled;

    public void AddRestRequest(string ownerLogin, Guid entityId, double durationMs) =>
        inner.AddRestRequest(ownerLogin, entityId, durationMs);

    public void AddMcpRequest(string ownerLogin, Guid entityId, double durationMs) =>
        inner.AddMcpRequest(ownerLogin, entityId, durationMs);

    public void AddAuthenticationFailure() => inner.AddAuthenticationFailure();
}
