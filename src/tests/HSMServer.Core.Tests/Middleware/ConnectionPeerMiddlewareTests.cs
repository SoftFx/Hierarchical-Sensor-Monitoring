using HSMServer.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.Middleware
{
    // The connection's own peer, recorded before UseForwardedHeaders replaces it, and judged against the same options.
    public class ConnectionPeerMiddlewareTests
    {
        private static readonly (IPAddress, int)[] AttachedCompose = [(IPAddress.Parse("172.30.244.5"), 24)];


        private static async Task<HttpContext> Send(ForwardedHeadersOptions options, string peerIp, bool forgeOriginalFor = false)
        {
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Parse(peerIp);
            context.Request.Headers["X-Forwarded-For"] = "203.0.113.7";
            if (forgeOriginalFor)
                context.Request.Headers[ForwardedHeadersDefaults.XOriginalForHeaderName] = "172.30.244.3:5000";

            var forwarding = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance, Options.Create(options));
            var peer = new ConnectionPeerMiddleware(forwarding.Invoke, options);

            await peer.Invoke(context);

            return context;
        }


        [Theory]
        [InlineData("172.30.244.3", true)]           // caddy on the compose network
        [InlineData("::ffff:172.30.244.3", true)]    // the same peer on a dual-stack socket
        [InlineData("172.30.245.3", false)]          // another docker network
        [InlineData("198.51.100.20", false)]         // a direct client
        [InlineData("127.0.0.1", false)]             // loopback is not trusted implicitly
        public async Task ThePeerIsJudgedByTheOptionsOfTheForwardedHeaders(string peerIp, bool trusted)
        {
            var options = TrustedProxyOptionsFactory.Build([TrustedProxyOptionsFactory.AttachedNetworksKeyword], () => AttachedCompose);

            var context = await Send(options, peerIp, forgeOriginalFor: true);

            Assert.Equal(trusted, ConnectionPeerMiddleware.CameThroughTrustedProxy(context));
            Assert.Equal(IPAddress.Parse(peerIp), ConnectionPeerMiddleware.GetPeerAddress(context));
        }

        [Fact]
        public async Task ThePeerIsRecordedBeforeTheForwardedHeadersReplaceIt()
        {
            var options = TrustedProxyOptionsFactory.Build(["172.30.244.2"], () => []);

            var context = await Send(options, "172.30.244.2");

            Assert.Equal(IPAddress.Parse("203.0.113.7"), context.Connection.RemoteIpAddress);
            Assert.Equal(IPAddress.Parse("172.30.244.2"), ConnectionPeerMiddleware.GetPeerAddress(context));
            Assert.True(ConnectionPeerMiddleware.CameThroughTrustedProxy(context));
        }

        [Fact]
        public void NothingRecorded_IsNotATrustedProxy()
        {
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Parse("172.30.244.2");
            context.Request.Headers[ForwardedHeadersDefaults.XOriginalForHeaderName] = "172.30.244.2:5000";

            Assert.False(ConnectionPeerMiddleware.CameThroughTrustedProxy(context));
            Assert.Null(ConnectionPeerMiddleware.GetPeerAddress(context));
        }
    }
}
