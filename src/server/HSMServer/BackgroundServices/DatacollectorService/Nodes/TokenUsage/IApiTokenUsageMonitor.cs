using System;

namespace HSMServer.BackgroundServices;

// The monitoring surface the measurement middleware depends on (#1402) —
// the sensors registry behind it lives inside DataCollectorWrapper; the
// interface keeps the middleware unit-testable against a mock instead of a
// booted collector. The entityId is a Guid end-to-end (no string
// round-trip to parse on the request path); the tokenId is the LIVENESS
// key for the eviction sweep — kept in node memory only, never in the
// tree (ADR-0006).
public interface IApiTokenUsageMonitor
{
    // False when self-monitoring is disabled (MonitoringOptions): the
    // middleware skips measurement entirely — no lookups, no sensor
    // registration into a collector that will never publish (#1403 r3).
    bool Enabled { get; }

    void AddRestRequest(string tokenId, string ownerLogin, Guid entityId, double durationMs);

    void AddMcpRequest(string tokenId, string ownerLogin, Guid entityId, double durationMs);

    void AddAuthenticationFailure();
}
