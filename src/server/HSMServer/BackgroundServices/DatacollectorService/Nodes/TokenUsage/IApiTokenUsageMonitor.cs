namespace HSMServer.BackgroundServices;

// The monitoring surface the measurement middleware depends on (#1402) —
// the sensors registry behind it lives inside DataCollectorWrapper; the
// interface keeps the middleware unit-testable against a mock instead of a
// booted collector.
public interface IApiTokenUsageMonitor
{
    void AddRestRequest(string ownerLogin, string entityId, double durationMs);

    void AddMcpRequest(string ownerLogin, string entityId, double durationMs);

    void AddAuthenticationFailure();
}
