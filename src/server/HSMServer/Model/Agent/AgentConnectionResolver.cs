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

                if (Uri.TryCreate(raw, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
                {
                    // Keep scheme + host only; the port travels separately in config.json. A path base
                    // is deliberately NOT preserved: the native collector's endpoint builder
                    // (hsm_http_endpoints.hpp / HostOnly) strips everything after the first '/', so a
                    // reverse-proxy prefix could never reach the wire. Emitting it here would only
                    // bake a misleading address that silently connects to "<host>/api/sensors".
                    // Uri.Host already brackets IPv6 literals ("[::1]"), so the address stays well-formed.
                    var address = $"{uri.Scheme}://{uri.Host}";

                    // Use the admin's explicit port when present — even when it equals the scheme
                    // default (e.g. :443). Uri.IsDefaultPort can't tell "no port" from "explicit
                    // default port", so inspect the raw authority instead. Only an absent port falls
                    // back to the configured Sensor port.
                    var port = HasExplicitPort(raw) ? uri.Port : sensorPort;
                    return (address, port);
                }
            }

            return ($"{fallbackScheme}://{fallbackHost}", sensorPort);
        }

        private static bool HasExplicitPort(string raw)
        {
            var schemeIndex = raw.IndexOf("://", StringComparison.Ordinal);
            var authorityStart = schemeIndex >= 0 ? schemeIndex + 3 : 0;

            var authorityEnd = raw.IndexOf('/', authorityStart);
            var authority = authorityEnd >= 0 ? raw.Substring(authorityStart, authorityEnd - authorityStart) : raw.Substring(authorityStart);

            var at = authority.LastIndexOf('@'); // strip userinfo
            if (at >= 0)
                authority = authority.Substring(at + 1);

            int colon;
            if (authority.StartsWith("[", StringComparison.Ordinal)) // [IPv6]:port
            {
                var close = authority.IndexOf(']');
                colon = close >= 0 ? authority.IndexOf(':', close) : -1;
            }
            else
            {
                colon = authority.LastIndexOf(':');
            }

            return colon >= 0 && int.TryParse(authority.Substring(colon + 1), out _);
        }
    }
}
