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
    /// client agent has no UI and simply runs whatever config.json the bundle ships. The Linux probe
    /// bundle honours it the same way (#1479). Default false.
    /// </summary>
    public bool EnableTopCpuProcesses { get; set; }

    // There is no server-side switch for agent self-update (#1812): the client turns it off, with
    // update.enabled in its config.json. A settings file that still carries the retired
    // "AutoUpdateEnabled" key loads as before; the binder ignores keys without a property, and the
    // next settings save drops it.
}
