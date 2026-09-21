using System;

namespace HSMServer.BackgroundServices;

// The add-surface of the API-token usage monitoring (#1402) — the sensors
// registry behind it lives inside DataCollectorWrapper, and the measurement
// middleware consumes it through the gate half below (the only half DI
// publishes), which keeps the middleware unit-testable against a mock
// instead of a booted collector. The entityId is a Guid end-to-end (no
// string round-trip to parse on the request path) and is the ONLY token
// identifier on the surface — the public lifecycle identity; the TokenId
// (the authentication lookup key) never crosses it (ADR-0006: the sweep
// resolves liveness by EntityId through IApiTokenManager).
public interface IApiTokenUsageMonitor
{
    void AddRestRequest(string ownerLogin, Guid entityId, double durationMs);

    void AddMcpRequest(string ownerLogin, Guid entityId, double durationMs);

    void AddAuthenticationFailure();
}

// The gate half, split from the add-surface (#1403 review): the raw
// registry (ApiTokenUsageSensors) implements only IApiTokenUsageMonitor —
// it has no gate to expose (it is always on; the gate belongs to whoever
// owns the collector lifecycle). Keeping Enabled off the add-surface
// makes the compiler route every Enabled reader to the adapter
// (MonitoringGate, the live MonitoringOptions value) instead of relying
// on a runtime throw from the registry.
public interface IApiTokenUsageGate : IApiTokenUsageMonitor
{
    // False when self-monitoring is disabled (MonitoringOptions): the
    // middleware skips measurement entirely — no lookups, no sensor
    // registration into a collector that will never publish (#1403).
    bool Enabled { get; }
}
