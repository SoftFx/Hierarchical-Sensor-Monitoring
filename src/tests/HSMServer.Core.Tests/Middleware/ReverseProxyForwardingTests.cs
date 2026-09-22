using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.Middleware
{
    // Client IP behind the bundled Caddy (#1427): X-Forwarded-For is honoured only from the
    // configured proxies, so the token audit, the invalid-attempt limiter and key telemetry see
    // the client, and nobody else can choose the address the server records.
    public class ReverseProxyForwardingTests
    {
        private const string ComposeNetwork = "172.30.244.0/24";

        // What the server's own interfaces report inside the compose container: its address and prefix.
        private static readonly (IPAddress, int)[] AttachedCompose = [(IPAddress.Parse("172.30.244.5"), 24)];
        private const string Client = "203.0.113.7";


        private static async Task<HttpContext> SendThroughForwarding(KestrelConfig config, string peerIp, string scheme = "https")
        {
            var context = new DefaultHttpContext();
            context.Request.Scheme = scheme;
            context.Connection.RemoteIpAddress = IPAddress.Parse(peerIp);
            context.Request.Headers["X-Forwarded-For"] = Client;
            context.Request.Headers["X-Forwarded-Proto"] = "http";

            var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance,
                Options.Create(config.BuildForwardedHeadersOptions(() => AttachedCompose)));

            await middleware.Invoke(context);

            return context;
        }


        [Fact]
        public async Task TrustedProxy_RestoresTheClientAddress()
        {
            var context = await SendThroughForwarding(new KestrelConfig { TrustedProxies = [ComposeNetwork] }, "172.30.244.3");

            Assert.Equal(IPAddress.Parse(Client), context.Connection.RemoteIpAddress);
        }

        [Fact]
        public async Task TrustedProxy_DoesNotTouchTheScheme()
        {
            // The proxy-to-server hop is HTTPS; a forwarded Proto must not downgrade it.
            var context = await SendThroughForwarding(new KestrelConfig { TrustedProxies = [ComposeNetwork] }, "172.30.244.3");

            Assert.Equal("https", context.Request.Scheme);
        }

        [Theory]
        [InlineData("198.51.100.20")] // direct public client
        [InlineData("172.30.245.3")]  // private, but outside the configured network
        [InlineData("127.0.0.1")]     // the framework's implicit loopback trust is removed
        public async Task UntrustedPeer_CannotChooseTheRecordedAddress(string peerIp)
        {
            var context = await SendThroughForwarding(new KestrelConfig { TrustedProxies = [ComposeNetwork] }, peerIp);

            Assert.Equal(IPAddress.Parse(peerIp), context.Connection.RemoteIpAddress);
        }

        [Fact]
        public async Task SingleProxyAddress_IsTrustedExactly()
        {
            var config = new KestrelConfig { TrustedProxies = ["172.30.244.2"] };

            Assert.Equal(IPAddress.Parse(Client), (await SendThroughForwarding(config, "172.30.244.2")).Connection.RemoteIpAddress);
            Assert.Equal(IPAddress.Parse("172.30.244.9"), (await SendThroughForwarding(config, "172.30.244.9")).Connection.RemoteIpAddress);
        }

        [Theory]
        [InlineData("172.30.244.3", true)]   // caddy on the same compose network
        [InlineData("172.30.245.3", false)]  // another docker network on the host
        [InlineData("10.0.0.7", false)]
        public async Task AttachedNetworks_TrustsOnlyTheServersOwnNetwork(string peerIp, bool trusted)
        {
            var context = await SendThroughForwarding(new KestrelConfig { TrustedProxies = [KestrelConfig.AttachedNetworksKeyword] }, peerIp);

            Assert.Equal(IPAddress.Parse(trusted ? Client : peerIp), context.Connection.RemoteIpAddress);
        }

        [Fact]
        public void AttachedNetworks_AreRegisteredAsTheirNetworkAddress()
        {
            var options = new KestrelConfig { TrustedProxies = [KestrelConfig.AttachedNetworksKeyword] }
                .BuildForwardedHeadersOptions(() => AttachedCompose);

            Assert.Equal("172.30.244.0/24", KestrelConfig.Describe(options));
        }

        [Theory]
        [InlineData("172.30.244.5", 24, "172.30.244.0")]
        [InlineData("172.30.244.5", 20, "172.30.240.0")]
        [InlineData("10.1.2.3", 0, "0.0.0.0")]
        [InlineData("10.1.2.3", 32, "10.1.2.3")]
        [InlineData("fd00::1:2", 64, "fd00::")]
        public void MaskToNetwork_ClearsHostBits(string address, int prefix, string expected)
        {
            Assert.Equal(IPAddress.Parse(expected), KestrelConfig.MaskToNetwork(IPAddress.Parse(address), prefix));
        }

        [Fact]
        public void Default_TrustsNobody()
        {
            Assert.Empty(new KestrelConfig().TrustedProxies);
        }

        [Fact]
        public void TrustedProxies_IsNotWrittenBackToTheSettingsFile()
        {
            // Deployment-owned (compose environment): persisting it would let a stale file shadow
            // a changed compose network, because the settings file overrides the environment.
            var json = System.Text.Json.JsonSerializer.Serialize(new KestrelConfig { TrustedProxies = [ComposeNetwork] });

            Assert.DoesNotContain(nameof(KestrelConfig.TrustedProxies), json);
        }

        [Theory]
        [InlineData("not-an-ip")]
        [InlineData("10.0.0.0/33")]
        [InlineData("10.0.0.0/abc")]
        [InlineData("10.0.0.0/8/1")]
        [InlineData("")]
        public void Validate_RejectsMalformedEntry(string entry)
        {
            var error = Assert.Throws<InvalidOperationException>(new KestrelConfig { TrustedProxies = [entry] }.Validate);

            Assert.Contains("Kestrel.TrustedProxies", error.Message);
        }

        [Fact]
        public void Validate_AcceptsAddressesAndNetworks()
        {
            new KestrelConfig { TrustedProxies = ["172.30.244.2", ComposeNetwork, "fd00::/8", "::1", "attached-networks", "Attached-Networks"] }.Validate();
        }

        [Fact]
        public void Pipeline_RestoresTheClientAddressBeforeAnythingReadsIt()
        {
            // Registered lower, authentication (token audit and the invalid-attempt limiter)
            // and the telemetry middleware would record the proxy instead of the client.
            var body = ReadConfigureMiddlewareBody();

            var forwarded = body.IndexOf("UseForwardedHeaders", StringComparison.Ordinal);

            Assert.True(forwarded >= 0, "UseForwardedHeaders is not registered in ConfigureMiddleware");

            foreach (var later in new[] { "UseExceptionHandler", "UseHttpsRedirection", "UseAuthentication", "ApiTokenUsageMiddleware", "TelemetryMiddleware" })
                Assert.True(body.IndexOf(later, StringComparison.Ordinal) > forwarded, $"{later} must come after UseForwardedHeaders");
        }


        private static string ReadConfigureMiddlewareBody()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
                directory = directory.Parent;

            Assert.NotNull(directory);

            var source = File.ReadAllText(Path.Combine(directory.FullName, "src", "server", "HSMServer", "Extensions", "ApplicationServiceExtensions.cs"));
            var start = source.IndexOf("ConfigureMiddleware", StringComparison.Ordinal);
            var end = source.IndexOf("InitStorages", start, StringComparison.Ordinal);

            return string.Join("\n", source[start..end].Split('\n').Where(line => !line.TrimStart().StartsWith("//")));
        }
    }
}
