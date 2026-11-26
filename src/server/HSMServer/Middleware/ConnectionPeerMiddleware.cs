using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace HSMServer.Middleware
{
    /// <summary>
    /// Records the connection's own peer address, and whether it is one of the trusted proxies, before
    /// UseForwardedHeaders replaces <see cref="ConnectionInfo.RemoteIpAddress"/> with the address the proxy stated.
    /// </summary>
    // Registered right before UseForwardedHeaders with the SAME options instance, so "trusted proxy" means exactly the
    // peers whose X-Forwarded-For the app honours. The decision comes from the connection itself, never from a request
    // header. Not registered (no trusted proxy configured, or a host without it) means no request came through a trusted
    // proxy.
    public sealed class ConnectionPeerMiddleware
    {
        private static readonly object ItemKey = new();

        private readonly RequestDelegate _next;
        private readonly ForwardedHeadersOptions _options;


        public ConnectionPeerMiddleware(RequestDelegate next, ForwardedHeadersOptions options)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }


        public Task Invoke(HttpContext context)
        {
            var peer = context.Connection.RemoteIpAddress;

            context.Items[ItemKey] = new ConnectionPeer(peer, IsTrustedProxy(peer, _options));

            return _next(context);
        }


        /// <summary>True when the request's connection came from a trusted proxy (recorded by this middleware).</summary>
        public static bool CameThroughTrustedProxy(HttpContext context) =>
            context?.Items.TryGetValue(ItemKey, out var recorded) == true && recorded is ConnectionPeer { IsTrustedProxy: true };

        /// <summary>The connection's own peer address, before forwarded headers were applied; null when not recorded.</summary>
        public static IPAddress GetPeerAddress(HttpContext context) =>
            context?.Items.TryGetValue(ItemKey, out var recorded) == true && recorded is ConnectionPeer peer ? peer.Address : null;

        // The check of ForwardedHeadersMiddleware: an IPv4-mapped peer is compared as IPv4, then the known proxies
        // and the known networks.
        internal static bool IsTrustedProxy(IPAddress address, ForwardedHeadersOptions options)
        {
            if (address is null || options is null)
                return false;

            if (address.IsIPv4MappedToIPv6 && IsTrustedProxy(address.MapToIPv4(), options))
                return true;

            return options.KnownProxies.Contains(address) || options.KnownNetworks.Any(network => network.Contains(address));
        }


        private sealed record ConnectionPeer(IPAddress Address, bool IsTrustedProxy);
    }
}
