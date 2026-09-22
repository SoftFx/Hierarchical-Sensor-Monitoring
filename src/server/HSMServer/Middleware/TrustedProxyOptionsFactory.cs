using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using AspNetIPNetwork = Microsoft.AspNetCore.HttpOverrides.IPNetwork;

namespace HSMServer.Middleware
{
    // Turns Kestrel.TrustedProxies into the options of UseForwardedHeaders (#1427): which
    // peers may state the client address in X-Forwarded-For. Entries are an IP, a CIDR
    // network, or AttachedNetworksKeyword.
    public static class TrustedProxyOptionsFactory
    {
        // "The networks this server's own interfaces are on" (loopback excluded). In
        // docker-compose.yml that is the compose network: no fixed subnet has to be picked,
        // since a pinned range collides with the /16s Docker hands out to other projects.
        // The range includes the bridge gateway, so processes on the Docker host (and any
        // container joined to the network) can state the client address too.
        public const string AttachedNetworksKeyword = "attached-networks";


        public static bool IsValidEntry(string entry) => IsAttachedNetworksKeyword(entry) || TryParseEntry(entry, out _, out _);

        public static ForwardedHeadersOptions Build(IEnumerable<string> trustedProxies) => Build(trustedProxies, GetAttachedNetworks);

        public static ForwardedHeadersOptions Build(IEnumerable<string> trustedProxies, Func<IEnumerable<(IPAddress Address, int PrefixLength)>> attachedNetworks)
        {
            var options = new ForwardedHeadersOptions
            {
                // Only the client address: the proxy-to-server hop is HTTPS already, so the scheme
                // needs no restoring. Two independent guards against a client-chosen address:
                // Caddy (no trusted_proxies) replaces any incoming X-Forwarded-For with the peer
                // it saw, and ForwardLimit = 1 takes only the rightmost entry. Raising the limit
                // (a second proxy hop) needs the outer hop's own sanitising first.
                ForwardedHeaders = ForwardedHeaders.XForwardedFor,
                ForwardLimit = 1,
            };

            // The framework trusts loopback by default; only the configured proxies may forward.
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();

            foreach (var entry in trustedProxies)
            {
                if (IsAttachedNetworksKeyword(entry))
                {
                    foreach (var (address, prefixLength) in attachedNetworks())
                        options.KnownNetworks.Add(new AspNetIPNetwork(MaskToNetwork(address, prefixLength), prefixLength));
                }
                else if (TryParseEntry(entry, out var address, out var prefix))
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

        // IPNetwork.Contains compares its Prefix with the masked address, so host bits must be cleared.
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

        private static bool TryParseEntry(string entry, out IPAddress address, out int? prefix)
        {
            prefix = null;
            var parts = (entry ?? string.Empty).Trim().Split('/');

            // IPAddress.TryParse also takes shorthands ("192.168" is 0.0.192.168): a truncated
            // entry must fail validation, not register an address that never matches.
            if (parts.Length > 2 || !IPAddress.TryParse(parts[0], out address)
                || address.AddressFamily == AddressFamily.InterNetwork && parts[0].Split('.').Length != 4)
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
}
