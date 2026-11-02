using HSMServer.Middleware;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.Middleware
{
    // The deprecated-port signal of the #1590 deprecation window: a request whose Host
    // carries 44330/44333 (the aliases still published through the window) logs a
    // rate-limited warning — the operator's signal for stragglers — while ordinary
    // port-less 443 traffic stays silent, and so does a legacy port that is the
    // server's own listener (a proxy-less deployment pinned to the old address).
    public class LegacyPortDeprecationMiddlewareTests
    {
        [Theory]
        [InlineData(KestrelConfig.LegacySensorPort)]
        [InlineData(KestrelConfig.LegacySitePort)]
        public async Task LegacyHostPort_LogsWarningWithRemoteIp_AndPassesThrough(int port)
        {
            var logs = new RecordingLogger();
            var context = Context(host: $"hsm.example.com:{port}");

            await Middleware(logs).InvokeAsync(context);

            Assert.True(context.Items.ContainsKey("reached"));
            Assert.Contains(logs.Entries, e => e.Contains("deprecated") && e.Contains(port.ToString()));
            // The remote IP makes the warning actionable: that is the straggler to repoint
            // (already the client address behind the trusted-proxy forwarded headers).
            Assert.Contains(logs.Entries, e => e.Contains("127.0.0.1"));
        }

        [Theory]
        [InlineData("hsm.example.com")]        // port-less https://host — the primary address
        [InlineData("hsm.example.com:443")]    // explicit default port
        [InlineData("hsm.example.com:8443")]   // a non-legacy custom port
        public async Task NonLegacyHost_LogsNothing(string host)
        {
            var logs = new RecordingLogger();
            var context = Context(host);

            await Middleware(logs).InvokeAsync(context);

            Assert.True(context.Items.ContainsKey("reached"));
            Assert.Empty(logs.Entries);
        }

        [Theory]
        [InlineData(KestrelConfig.LegacySensorPort)]
        [InlineData(KestrelConfig.LegacySitePort)]
        public async Task LegacyPortThatIsTheOwnListener_LogsNothing(int port)
        {
            // Kestrel.Port pinned to the old address, no proxy in front: the legacy port in
            // the Host header is the configured listener, not a straggler through an alias —
            // warning on it would never go quiet and corrupt the phase-2 signal.
            var logs = new RecordingLogger();
            var context = Context(host: $"hsm.example.com:{port}");

            await Middleware(logs, listenerPort: port).InvokeAsync(context);

            Assert.True(context.Items.ContainsKey("reached"));
            Assert.Empty(logs.Entries);
        }

        [Fact]
        public void Throttle_SecondLegacyRequestInsideTheWindow_LogsNothing()
        {
            var logs = new RecordingLogger();
            var middleware = Middleware(logs);

            var start = DateTime.UtcNow.Ticks;

            Assert.True(middleware.ShouldWarn(start));
            Assert.False(middleware.ShouldWarn(start + TimeSpan.FromSeconds(1).Ticks));
            Assert.False(middleware.ShouldWarn(start + TimeSpan.FromMinutes(9).Ticks));

            // Past the window the warning fires again.
            Assert.True(middleware.ShouldWarn(start + TimeSpan.FromMinutes(11).Ticks));
        }


        private static LegacyPortDeprecationMiddleware Middleware(RecordingLogger logs, int listenerPort = KestrelConfig.DefaultPort)
        {
            var config = new Mock<IServerConfig>();
            config.Setup(c => c.Kestrel).Returns(new KestrelConfig { Port = listenerPort });

            return new LegacyPortDeprecationMiddleware(NextThatMarks, config.Object, logs);
        }

        private static DefaultHttpContext Context(string host)
        {
            var context = new DefaultHttpContext();
            context.Request.Host = new HostString(host);
            context.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
            return context;
        }

        private static Task NextThatMarks(HttpContext context)
        {
            context.Items["reached"] = true;
            return Task.CompletedTask;
        }


        private sealed class RecordingLogger : ILogger<LegacyPortDeprecationMiddleware>
        {
            public List<string> Entries { get; } = [];

            public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter) =>
                Entries.Add(formatter(state, exception));
        }
    }
}
