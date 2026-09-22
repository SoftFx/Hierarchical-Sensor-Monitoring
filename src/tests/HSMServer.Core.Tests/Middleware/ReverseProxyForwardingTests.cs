using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
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


        private static async Task<HttpContext> SendThroughForwarding(KestrelConfig config, string peerIp, string forwardedFor = Client)
        {
            var context = new DefaultHttpContext();
            context.Request.Scheme = "https";
            context.Connection.RemoteIpAddress = IPAddress.Parse(peerIp);
            context.Connection.RemotePort = 51234;
            context.Request.Headers["X-Forwarded-For"] = forwardedFor;
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
        public async Task TrustedProxy_TakesOnlyTheRightmostEntry()
        {
            // Caddy replaces a client-sent X-Forwarded-For, but should an extra entry ever
            // reach the server (another proxy hop in front), ForwardLimit = 1 still takes only
            // the entry the trusted proxy appended, never one the client chose.
            var context = await SendThroughForwarding(new KestrelConfig { TrustedProxies = [ComposeNetwork] }, "172.30.244.3", $"6.6.6.6, {Client}");

            Assert.Equal(IPAddress.Parse(Client), context.Connection.RemoteIpAddress);
        }

        [Fact]
        public async Task TrustedProxy_ForwardedAddressCarriesNoPort()
        {
            // X-Forwarded-For has no port, so the token audit source must not print the proxy's
            // ephemeral port next to the client address (HsmApiTokenHandler.DescribeSource).
            var context = await SendThroughForwarding(new KestrelConfig { TrustedProxies = [ComposeNetwork] }, "172.30.244.3");

            Assert.Equal(0, context.Connection.RemotePort);
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
        public void ReadTrustedProxies_DropsBlankEntries()
        {
            // `Kestrel__TrustedProxies__0: ''` is how the recovery override switches trust off.
            var environment = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
            {
                ["Kestrel:TrustedProxies:0"] = "",
                ["Kestrel:TrustedProxies:1"] = " ",
            }).Build();

            Assert.Empty(KestrelConfig.ReadTrustedProxies(environment));
        }

        [Fact]
        public void ReadTrustedProxies_ReadsTheEnvironmentValue()
        {
            var environment = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
            {
                ["Kestrel:TrustedProxies:0"] = KestrelConfig.AttachedNetworksKeyword,
            }).Build();

            Assert.Equal([KestrelConfig.AttachedNetworksKeyword], KestrelConfig.ReadTrustedProxies(environment));
        }

        [Theory]
        [InlineData("{ \"Kestrel\": { \"SitePort\": 44333, \"TrustedProxies\": [ \"10.1.0.0/24\" ] } }", true)]
        [InlineData("{ \"Kestrel\": { \"SitePort\": 44333 } }", false)]
        public void SettingsFileHasTrustedProxies_DetectsTheIgnoredFileValue(string json, bool expected)
        {
            var path = Path.Combine(Path.GetTempPath(), $"hsm-trusted-proxies-{Guid.NewGuid():N}.json");
            File.WriteAllText(path, json);

            try
            {
                var configuration = new ConfigurationBuilder().AddJsonFile(path).Build();

                Assert.Equal(expected, KestrelConfig.SettingsFileHasTrustedProxies(configuration));
            }
            finally
            {
                File.Delete(path);
            }
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
            // First within ConfigureMiddleware (Program.cs registers only request localization
            // before it, which never reads the peer address). Registered lower, authentication
            // (token audit and the invalid-attempt limiter) and the telemetry middleware would
            // record the proxy instead of the client.
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
            var end = start < 0 ? -1 : source.IndexOf("return applicationBuilder;", start, StringComparison.Ordinal);

            Assert.True(start >= 0 && end > start, "Cannot locate the ConfigureMiddleware body in ApplicationServiceExtensions.cs; update this test.");

            return string.Join("\n", source[start..end].Split('\n').Where(line => !line.TrimStart().StartsWith("//")));
        }
    }
}
