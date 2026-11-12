#if NET5_0_OR_GREATER
#nullable disable
#endif

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Client;
using HSMDataCollector.Core;
using HSMDataCollector.IntegrationTests.Helpers;
using HSMDataCollector.Logging;
using HSMSensorDataObjects.SensorValueRequests;
using Xunit;

namespace HSMDataCollector.Tests.Transport
{
    /// <summary>
    /// #1659 B1: the server directive channel (#1198) in the managed collector. A host can add
    /// request headers to the value requests, and the collector reads the
    /// <c>X-Hsm-Directive</c> headers of the answers. It handles them inside: there is no public
    /// event, and the library acts on none of today's directives, so it notes each unknown one
    /// once and moves on.
    ///
    /// Compiled into both collector test projects (net472 and net6.0 builds of the collector).
    /// </summary>
    [Trait("Category", "FakeServerE2E")]
    public sealed class DirectiveChannelTests
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

        private static CollectorOptions OptionsFor(FakeHsmServer server)
        {
            return new CollectorOptions
            {
                ServerAddress = server.ServerAddress,
                Port = server.Port,
                AccessKey = "directive-key",
                ClientName = "directive-client",
                AllowPlaintextTransport = true,
                PackageCollectPeriod = TimeSpan.FromMilliseconds(100),
            };
        }

        [Fact]
        public async Task Extra_request_headers_travel_with_value_requests_only()
        {
            using (var server = new FakeHsmServer())
            {
                var options = OptionsFor(server);
                options.ExtraRequestHeaders["X-Agent-Version"] = "1.2.3";

                using (var collector = new DataCollector(options))
                {
                    var sensor = collector.CreateIntSensor("directive/headers");
                    await collector.Start();

                    sensor.AddValue(5);

                    Assert.True(await WaitForAsync(() => server.DataRequests.Any(r => r.Body.Contains("directive/headers"))), "The value never arrived.");
                    Assert.True(await WaitForAsync(() => server.CommandRequests.Any(r => r.Body.Contains("directive/headers"))), "The registration never arrived.");

                    await collector.Stop();
                }

                var value = server.DataRequests.First(r => r.Body.Contains("directive/headers"));
                var registration = server.CommandRequests.First(r => r.Body.Contains("directive/headers"));

                Assert.True(value.Headers.TryGetValue("X-Agent-Version", out var sent), "The value request carries no X-Agent-Version.");
                Assert.Equal("1.2.3", sent);
                Assert.Equal("directive-key", value.Headers["Key"]);

                // As in the native collector: the headers are for the route that answers with
                // directives. A registration carries the access headers only.
                Assert.False(registration.Headers.ContainsKey("X-Agent-Version"));
            }
        }

        [Fact]
        public async Task Directives_of_a_value_response_are_received_and_an_unknown_one_is_noted_once()
        {
            var logger = new RecordingLogger();
            var log = new LoggerManager();
            log.AddLogger(logger);

            using (var server = new FakeHsmServer())
            using (var client = new HsmHttpsClient(OptionsFor(server), log))
            {
                server.SendDirectivesOnDataResponses("sensor-disable:network", "update-available:9.9.9");

                for (var i = 0; i < 3; i++)
                {
                    var info = await client.SendDataAsync(new[] { new IntSensorValue { Path = "directive/int", Value = i } }, CancellationToken.None);
                    Assert.True(info.IsSuccess, info.Error);
                }

                Assert.Equal(6, client.Directives.ReceivedCount);
            }

            var notes = logger.Debugs.Where(d => d.Contains("directive")).ToList();

            Assert.True(notes.Count == 2, "Expected one note per unknown directive, got: " + string.Join(" | ", notes));
            Assert.Contains(notes, n => n.Contains("sensor-disable"));
            Assert.Contains(notes, n => n.Contains("update-available"));
            Assert.Empty(logger.Errors);
        }

        [Fact]
        public async Task Directives_of_a_refused_response_are_ignored()
        {
            using (var server = new FakeHsmServer())
            using (var client = new HsmHttpsClient(OptionsFor(server), new LoggerManager()))
            {
                server.SendDirectivesOnDataResponses("sensor-disable:network");
                server.AnswerNextDataRequest(HttpStatusCode.NotAcceptable, "Access key is blocked.");

                var info = await client.SendDataAsync(new[] { new IntSensorValue { Path = "directive/int", Value = 1 } }, CancellationToken.None);

                Assert.False(info.IsSuccess);
                Assert.Equal(0, client.Directives.ReceivedCount);
            }
        }

        [Theory]
        [InlineData("Key", "other-key")]
        [InlineData("clientname", "other-client")]
        [InlineData("Content-Type", "text/plain")]
        [InlineData("Host", "elsewhere")]
        [InlineData("Bad Name", "value")]
        [InlineData("", "value")]
        [InlineData("X-Injected", "value\r\nKey: other-key")]
        [InlineData("X-Null", null)]
        [InlineData("X-Not-Ascii", "\u0441\u0435\u0440\u0432\u0435\u0440")]
        [InlineData("X-Control", "a\u0001b")]
        [InlineData("Content-Language", "en")]
        [InlineData("Expires", "0")]
        public void Extra_request_header_the_collector_cannot_send_is_refused_at_construction(string name, string value)
        {
            var options = new CollectorOptions
            {
                AccessKey = "directive-key",
                ClientName = "directive-client",
            };
            options.ExtraRequestHeaders[name] = value;

            Assert.ThrowsAny<ArgumentException>(() => new DataCollector(options));
        }

        private static async Task<bool> WaitForAsync(Func<bool> done)
        {
            var deadline = DateTime.UtcNow + Timeout;

            while (!done())
            {
                if (DateTime.UtcNow >= deadline)
                    return false;

                await Task.Delay(20).ConfigureAwait(false);
            }

            return true;
        }

        private sealed class RecordingLogger : ICollectorLogger
        {
            private readonly List<string> _errors = new List<string>();
            private readonly List<string> _debugs = new List<string>();

            internal List<string> Errors
            {
                get
                {
                    lock (_errors)
                        return _errors.ToList();
                }
            }

            internal List<string> Debugs
            {
                get
                {
                    lock (_debugs)
                        return _debugs.ToList();
                }
            }

            public void Debug(string message)
            {
                lock (_debugs)
                    _debugs.Add(message);
            }

            public void Info(string message) { }

            public void Error(string message)
            {
                lock (_errors)
                    _errors.Add(message);
            }

            public void Error(Exception ex)
            {
                lock (_errors)
                    _errors.Add(ex.ToString());
            }
        }
    }
}
