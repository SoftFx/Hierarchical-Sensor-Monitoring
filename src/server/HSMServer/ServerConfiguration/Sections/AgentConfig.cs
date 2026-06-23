namespace HSMServer.ServerConfiguration;

/// <summary>
/// Settings for the downloadable HSM Agent (epic #1167). The externally-reachable Sensor-API base URL
/// is baked into each per-product agent bundle so the client connects with zero configuration. The
/// server cannot infer this behind Docker/NAT, so an admin sets it; when blank, the download endpoint
/// falls back to the request host + the configured Sensor port.
/// </summary>
public class AgentConfig
{
    public string ExternalConnectionUrl { get; set; } = string.Empty;
}
