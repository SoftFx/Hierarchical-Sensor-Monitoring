using HSMServer.Middleware;
using Microsoft.AspNetCore.Http;
using System.Net;
using System.Threading.Tasks;

namespace HSMServer.Core.Tests.Infrastructure
{
    /// <summary>
    /// Records a request's connection peer the way the app does when trusted proxies are configured: by running
    /// <see cref="ConnectionPeerMiddleware"/> on the context, with <see cref="TrustedProxy"/> as the one trusted proxy.
    /// </summary>
    internal static class ConnectionPeers
    {
        /// <summary>The configured trusted proxy, standing for the bundled Caddy.</summary>
        public const string TrustedProxy = "10.0.0.1";

        /// <summary>A client that connects to the server itself.</summary>
        public const string DirectClient = "198.51.100.20";

        /// <summary>The <c>Kestrel.TrustedProxies</c> value that makes <see cref="TrustedProxy"/> trusted.</summary>
        public static string[] TrustedProxies => [TrustedProxy];


        public static void Record(HttpContext context, string peer)
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(peer);

            var middleware = new ConnectionPeerMiddleware(_ => Task.CompletedTask, TrustedProxyOptionsFactory.Build(TrustedProxies));

            middleware.Invoke(context).GetAwaiter().GetResult();
        }
    }
}
