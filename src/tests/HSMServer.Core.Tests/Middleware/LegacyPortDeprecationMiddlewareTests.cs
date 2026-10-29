using HSMServer.Middleware;
using HSMServer.ServerConfiguration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.Middleware
{
    // The deprecated-port signal of the #1590 deprecation window: a request whose Host
    // carries 44330/44333 (the aliases still published through the window) logs a
    // rate-limited warning — the operator's signal for stragglers — while ordinary
    // port-less 443 traffic stays silent.
    public class LegacyPortDeprecationMiddlewareTests
    {
        [Theory]
        [InlineData(KestrelConfig.LegacySensorPort)]
        [InlineData(KestrelConfig.LegacySitePort)]
        public async Task LegacyHostPort_LogsWarning_AndPassesThrough(int port)
        {
            var logs = new RecordingLogger();
            var context = Context(host: $"hsm.example.com:{port}");

            await new LegacyPortDeprecationMiddleware(NextThatMarks, logs).InvokeAsync(context);

            Assert.True(context.Items.ContainsKey("reached"));
            Assert.Contains(logs.Entries, e => e.Contains("deprecated") && e.Contains(port.ToString()));
        }

        [Theory]
        [InlineData("hsm.example.com")]        // port-less https://host — the primary address
        [InlineData("hsm.example.com:443")]    // explicit default port
        [InlineData("hsm.example.com:8443")]   // a non-legacy custom port
        public async Task NonLegacyHost_LogsNothing(string host)
        {
            var logs = new RecordingLogger();
            var context = Context(host);

            await new LegacyPortDeprecationMiddleware(NextThatMarks, logs).InvokeAsync(context);

            Assert.True(context.Items.ContainsKey("reached"));
            Assert.Empty(logs.Entries);
        }

        [Fact]
        public void Throttle_SecondLegacyRequestInsideTheWindow_LogsNothing()
        {
            var logs = new RecordingLogger();
            var middleware = new LegacyPortDeprecationMiddleware(NextThatMarks, logs);

            var start = DateTime.UtcNow.Ticks;

            Assert.True(middleware.ShouldWarn(start));
            Assert.False(middleware.ShouldWarn(start + TimeSpan.FromSeconds(1).Ticks));
            Assert.False(middleware.ShouldWarn(start + TimeSpan.FromMinutes(9).Ticks));

            // Past the window the warning fires again.
            Assert.True(middleware.ShouldWarn(start + TimeSpan.FromMinutes(11).Ticks));
        }


        private static DefaultHttpContext Context(string host)
        {
            var context = new DefaultHttpContext();
            context.Request.Host = new HostString(host);
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
