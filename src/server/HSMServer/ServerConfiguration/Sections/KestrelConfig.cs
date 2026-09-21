using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using System;
using System.Net;
using AspNetIPNetwork = Microsoft.AspNetCore.HttpOverrides.IPNetwork;

namespace HSMServer.ServerConfiguration;

public class KestrelConfig
{
    public const int DefaultSensorPort = 44330;
    public const int DefaultSitePort = 44333;

    // Trusted by default in plain-HTTP mode: loopback plus the private ranges a proxy
    // sidecar lives in (Docker bridge networks are 172.16.0.0/12). Never reachable
    // from the public internet, so X-Forwarded-* from them cannot be spoofed remotely.
    private static readonly string[] DefaultTrustedProxies =
    [
        "127.0.0.0/8", "::1/128", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "fc00::/7",
    ];


    public int SensorPort { get; set; } = DefaultSensorPort;

    public int SitePort { get; set; } = DefaultSitePort;

    // false = plain HTTP on both ports, for running behind a TLS-terminating reverse
    // proxy (e.g. Caddy with Let's Encrypt). HSTS and the HTTPS redirect are then the
    // proxy's job, and X-Forwarded-Proto/For from a trusted proxy restore the client's
    // scheme and address. Default true keeps existing installs on HTTPS.
    public bool UseHttps { get; set; } = true;

    // Proxy addresses (IP or CIDR) whose X-Forwarded-* headers are honoured in
    // plain-HTTP mode. Empty = loopback + private networks. Default must stay an empty
    // array: the configuration binder appends to a pre-filled one on every load.
    public string[] TrustedProxies { get; set; } = [];


    public void Validate()
    {
        foreach (var entry in TrustedProxies)
            if (!TryParseProxy(entry, out _, out _))
                throw new InvalidOperationException(
                    $"Kestrel.{nameof(TrustedProxies)} contains '{entry}', which is neither an IP address nor a CIDR network (e.g. 172.18.0.2 or 172.16.0.0/12).");
    }

    public ForwardedHeadersOptions BuildForwardedHeadersOptions()
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor,
        };

        // The framework trusts loopback only; replace that with the explicit list.
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();

        foreach (var entry in TrustedProxies.Length > 0 ? TrustedProxies : DefaultTrustedProxies)
        {
            if (!TryParseProxy(entry, out var address, out var prefix))
                continue;

            if (prefix is int length)
                options.KnownNetworks.Add(new AspNetIPNetwork(address, length));
            else
                options.KnownProxies.Add(address);
        }

        return options;
    }


    private static bool TryParseProxy(string entry, out IPAddress address, out int? prefix)
    {
        prefix = null;
        var parts = (entry ?? string.Empty).Trim().Split('/');

        if (parts.Length > 2 || !IPAddress.TryParse(parts[0], out address))
        {
            address = null;
            return false;
        }

        if (parts.Length == 1)
            return true;

        var maxPrefix = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? 128 : 32;
        if (!int.TryParse(parts[1], out var length) || length < 0 || length > maxPrefix)
            return false;

        prefix = length;
        return true;
    }
}
