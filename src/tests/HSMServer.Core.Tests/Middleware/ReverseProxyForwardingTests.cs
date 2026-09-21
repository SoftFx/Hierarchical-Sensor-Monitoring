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
    // Plain-HTTP mode behind a TLS-terminating proxy (#1411): X-Forwarded-* is honoured
    // only from trusted proxies, so the https scheme the agent bundle and cookies rely on
    // is restored for the proxy sidecar and cannot be spoofed by a direct client.
    public class ReverseProxyForwardingTests
    {
        private static async Task<HttpContext> SendThroughForwarding(KestrelConfig config, string remoteIp)
        {
            var context = new DefaultHttpContext();
            context.Request.Scheme = "http";
            context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
            context.Request.Headers["X-Forwarded-Proto"] = "https";
            context.Request.Headers["X-Forwarded-For"] = "203.0.113.7";

            var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance,
                Options.Create(config.BuildForwardedHeadersOptions()));

            await middleware.Invoke(context);

            return context;
        }


        [Theory]
        [InlineData("172.18.0.5")]   // Docker bridge network
        [InlineData("10.1.2.3")]
        [InlineData("192.168.1.10")]
        [InlineData("127.0.0.1")]
        public async Task DefaultTrust_PrivateProxy_RestoresClientSchemeAndAddress(string proxyIp)
        {
            var context = await SendThroughForwarding(new KestrelConfig(), proxyIp);

            Assert.Equal("https", context.Request.Scheme);
            Assert.Equal(IPAddress.Parse("203.0.113.7"), context.Connection.RemoteIpAddress);
        }

        [Fact]
        public async Task DefaultTrust_PublicClient_CannotSpoofScheme()
        {
            var context = await SendThroughForwarding(new KestrelConfig(), "198.51.100.20");

            Assert.Equal("http", context.Request.Scheme);
            Assert.Equal(IPAddress.Parse("198.51.100.20"), context.Connection.RemoteIpAddress);
        }

        [Fact]
        public async Task ExplicitTrust_ReplacesTheDefaultRanges()
        {
            var config = new KestrelConfig { TrustedProxies = ["172.18.0.2", "203.0.113.0/24"] };

            Assert.Equal("https", (await SendThroughForwarding(config, "172.18.0.2")).Request.Scheme);
            Assert.Equal("https", (await SendThroughForwarding(config, "203.0.113.99")).Request.Scheme);
            Assert.Equal("http", (await SendThroughForwarding(config, "172.18.0.3")).Request.Scheme);
        }

        [Fact]
        public void Defaults_KeepHttpsOnAndNoExplicitProxies()
        {
            var config = new KestrelConfig();

            Assert.True(config.UseHttps);
            Assert.Empty(config.TrustedProxies);
        }

        [Theory]
        [InlineData("not-an-ip")]
        [InlineData("10.0.0.0/33")]
        [InlineData("10.0.0.0/abc")]
        [InlineData("10.0.0.0/8/1")]
        [InlineData("")]
        public void Validate_RejectsMalformedProxyEntry(string entry)
        {
            var config = new KestrelConfig { TrustedProxies = [entry] };

            var error = Assert.Throws<InvalidOperationException>(config.Validate);
            Assert.Contains("Kestrel.TrustedProxies", error.Message);
        }

        [Fact]
        public void Validate_AcceptsAddressesAndNetworks()
        {
            new KestrelConfig { TrustedProxies = ["172.18.0.2", "fd00::/8", "::1", "10.0.0.0/8"] }.Validate();
        }

        [Fact]
        public void Pipeline_RestoresForwardedHeadersBeforeAnythingReadsTheRequest()
        {
            // Registered anywhere lower, the exception page, HSTS and authentication would
            // run against the proxy's http request instead of the client's https one.
            var body = ReadConfigureMiddlewareBody();

            var forwarded = body.IndexOf("UseForwardedHeaders", StringComparison.Ordinal);

            Assert.True(forwarded >= 0, "UseForwardedHeaders is not registered in ConfigureMiddleware");

            foreach (var later in new[] { "UseDeveloperExceptionPage", "UseHsts", "UseExceptionHandler", "UseHttpsRedirection", "UseAuthentication" })
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
