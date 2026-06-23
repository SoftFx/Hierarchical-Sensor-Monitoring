using System;

namespace HSMServer.Model.Agent
{
    /// <summary>
    /// Resolves the (address, port) the agent bundle's config.json points at. Prefers the admin-set
    /// external URL (the externally-reachable Sensor-API base; the server can't infer it behind
    /// Docker/NAT); when blank, falls back to the request host + the configured Sensor port. Pure
    /// (primitives only) so it is unit-tested without a web host.
    /// </summary>
    public static class AgentConnectionResolver
    {
        public static (string Address, int Port) Resolve(string externalUrl, int sensorPort, string fallbackScheme, string fallbackHost)
        {
            if (!string.IsNullOrWhiteSpace(externalUrl))
            {
                var raw = externalUrl.Trim();
                if (!raw.Contains("://"))
                    raw = $"https://{raw}";

                if (Uri.TryCreate(raw, UriKind.Absolute, out var uri))
                {
                    var port = uri.IsDefaultPort ? sensorPort : uri.Port;
                    return ($"{uri.Scheme}://{uri.Host}", port);
                }
            }

            return ($"{fallbackScheme}://{fallbackHost}", sensorPort);
        }
    }
}
