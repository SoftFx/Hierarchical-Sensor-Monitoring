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

    /// <summary>
    /// Baked into downloaded bundles as <c>server.allowUntrustedCertificate</c> so an agent accepts a
    /// self-signed server certificate (the typical self-hosted / Docker eval case). Default false.
    /// </summary>
    public bool AllowUntrustedCertificate { get; set; }

    /// <summary>
    /// When true, downloaded bundles carry a <c>topCpu</c> block (issue #1175) so the installed agent
    /// also reports the top processes by CPU once a minute. The admin opts in here (server-side); the
    /// client agent has no UI and simply runs whatever config.json the bundle ships. Default false.
    /// </summary>
    public bool EnableTopCpuProcesses { get; set; }
}
