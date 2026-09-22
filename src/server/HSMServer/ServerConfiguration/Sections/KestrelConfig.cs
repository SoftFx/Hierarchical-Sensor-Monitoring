using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json.Serialization;
using AspNetIPNetwork = Microsoft.AspNetCore.HttpOverrides.IPNetwork;

namespace HSMServer.ServerConfiguration;

public class KestrelConfig
{
    public const int DefaultSensorPort = 44330;
    public const int DefaultSitePort = 44333;

    // TrustedProxies entry meaning "the networks this server's own interfaces are on"
    // (loopback excluded). In docker-compose.yml the only such network is the compose one,
    // whose only other member is Caddy, and no fixed subnet has to be picked (a pinned
    // range collides with the /16s Docker hands out to other projects on the host).
    public const string AttachedNetworksKeyword = "attached-networks";


    public int SensorPort { get; set; } = DefaultSensorPort;

    public int SitePort { get; set; } = DefaultSitePort;

    // Reverse proxies (IP, CIDR or AttachedNetworksKeyword) whose X-Forwarded-For is trusted
    // for the client address (#1427). Empty = no forwarded headers are honoured, so a server
    // reached directly keeps using the connection address. Deployment-owned: supplied by the
    // environment (docker-compose.yml) and never written back to appsettings.json, where a
    // stale copy would shadow the environment. The default must stay an empty array: the
    // configuration binder appends to a pre-filled one.
    [JsonIgnore]
    public string[] TrustedProxies { get; set; } = [];


    public void Validate()
    {
        foreach (var entry in TrustedProxies)
            if (!IsAttachedNetworksKeyword(entry) && !TryParseProxy(entry, out _, out _))
                throw new InvalidOperationException(
                    $"Kestrel.{nameof(TrustedProxies)} contains '{entry}', which is neither an IP address, a CIDR network (e.g. 172.18.0.0/16) nor '{AttachedNetworksKeyword}'.");
    }

    public ForwardedHeadersOptions BuildForwardedHeadersOptions() => BuildForwardedHeadersOptions(GetAttachedNetworks);

    public ForwardedHeadersOptions BuildForwardedHeadersOptions(Func<IEnumerable<(IPAddress Address, int PrefixLength)>> attachedNetworks)
    {
        var options = new ForwardedHeadersOptions
        {
            // Only the client address: the proxy-to-server hop is HTTPS already, so the scheme
            // needs no restoring. One hop: Caddy overwrites X-Forwarded-For with the peer it saw.
            ForwardedHeaders = ForwardedHeaders.XForwardedFor,
            ForwardLimit = 1,
        };

        // The framework trusts loopback by default; only the configured proxies may forward.
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();

        foreach (var entry in TrustedProxies)
        {
            if (IsAttachedNetworksKeyword(entry))
            {
                foreach (var (address, prefixLength) in attachedNetworks())
                    options.KnownNetworks.Add(new AspNetIPNetwork(MaskToNetwork(address, prefixLength), prefixLength));
            }
            else if (TryParseProxy(entry, out var address, out var prefix))
            {
                if (prefix is int length)
                    options.KnownNetworks.Add(new AspNetIPNetwork(MaskToNetwork(address, length), length));
                else
                    options.KnownProxies.Add(address);
            }
        }

        return options;
    }

    public static string Describe(ForwardedHeadersOptions options) =>
        string.Join(", ", options.KnownNetworks.Select(n => $"{n.Prefix}/{n.PrefixLength}").Concat(options.KnownProxies.Select(p => p.ToString())));

    public static IPAddress MaskToNetwork(IPAddress address, int prefixLength)
    {
        var bytes = address.GetAddressBytes();

        for (var i = 0; i < bytes.Length; i++)
        {
            var bits = Math.Clamp(prefixLength - i * 8, 0, 8);
            bytes[i] &= (byte)(0xFF << (8 - bits));
        }

        return new IPAddress(bytes);
    }


    private static IEnumerable<(IPAddress, int)> GetAttachedNetworks() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic => nic.OperationalStatus == OperationalStatus.Up && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
            .Where(unicast => !IPAddress.IsLoopback(unicast.Address) && !unicast.Address.IsIPv6LinkLocal && unicast.PrefixLength > 0)
            .Select(unicast => (unicast.Address, unicast.PrefixLength))
            .ToList();

    private static bool IsAttachedNetworksKeyword(string entry) =>
        string.Equals(entry?.Trim(), AttachedNetworksKeyword, StringComparison.OrdinalIgnoreCase);

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

        var maxPrefix = address.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32;
        if (!int.TryParse(parts[1], out var length) || length < 0 || length > maxPrefix)
            return false;

        prefix = length;
        return true;
    }
}
