#if NET5_0_OR_GREATER
#nullable disable
#endif

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
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

        private static CollectorOptions OptionsFor(FakeHsmServer server) => OptionsFor(server.ServerAddress, server.Port);

        private static CollectorOptions OptionsFor(string address, int port)
        {
            return new CollectorOptions
            {
                ServerAddress = address,
                Port = port,
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

        [Theory]
        [InlineData("Authorization", "Bearer abc")]
        [InlineData("User-Agent", "probe/1.0")]
        // Headers the .NET Framework handler has typed properties for, with values that do not
        // parse as their type: they are still sent as they were set.
        [InlineData("Date", "today")]
        [InlineData("Referer", "not a uri")]
        public async Task Extra_request_header_reaches_the_server_as_it_was_set(string name, string value)
        {
            using (var server = new FakeHsmServer())
            {
                var options = OptionsFor(server);
                options.ExtraRequestHeaders[name] = value;

                using (var collector = new DataCollector(options))
                {
                    var sensor = collector.CreateIntSensor("directive/as-set");
                    await collector.Start();

                    sensor.AddValue(5);

                    Assert.True(await WaitForAsync(() => server.DataRequests.Any(r => r.Body.Contains("directive/as-set"))), "The value never arrived.");

                    await collector.Stop();
                }

                var request = server.DataRequests.First(r => r.Body.Contains("directive/as-set"));

                Assert.True(request.Headers.TryGetValue(name, out var sent), $"The value request carries no {name}.");
                Assert.Equal(value, sent);
            }
        }

        [Fact]
        public async Task Directives_of_a_value_response_are_received_and_an_unknown_one_is_noted_once()
        {
            var logger = new RecordingLogger();
            var log = new LoggerManager();
            log.AddLogger(logger);

            // The answer is written by hand, one header line per directive, as the real server
            // writes them. A line is one directive, and HttpListener is free to send two headers
            // of one name as one comma-joined line (it does outside Windows), which would make
            // this test count lines of the listener instead of directives of the server.
            using (var server = new DirectiveLinesServer("sensor-disable:network", "update-available:9.9.9"))
            using (var client = new HsmHttpsClient(OptionsFor("http://127.0.0.1", server.Port), log))
            {
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

        [Fact]
        public void Noted_directive_names_are_bounded()
        {
            const int extra = 8;

            var logger = new RecordingLogger();
            var directives = new ServerDirectives(logger);

            // Twice: a name that was noted is not noted again, and a name that found no room
            // the first time finds none the second.
            for (var pass = 0; pass < 2; pass++)
                for (var i = 0; i < ServerDirectives.MaxNotedNames + extra; i++)
                    directives.Receive($"name-{i}:argument");

            var notes = logger.Debugs.Where(d => d.Contains("is not handled")).ToList();

            Assert.Equal(2 * (ServerDirectives.MaxNotedNames + extra), directives.ReceivedCount);
            Assert.Equal(ServerDirectives.MaxNotedNames, directives.NotedNamesCount);
            Assert.Equal(ServerDirectives.MaxNotedNames, notes.Count);

            // The first names to arrive are the ones that were noted, each once.
            for (var i = 0; i < ServerDirectives.MaxNotedNames; i++)
                Assert.Single(notes.Where(n => n.Contains($"'name-{i}'")));

            Assert.Empty(logger.Errors);
        }

        [Fact]
        public void Noted_directive_names_are_bounded_when_they_arrive_on_many_threads()
        {
            const int threads = 8;
            const int namesPerThread = 2 * ServerDirectives.MaxNotedNames;

            // Many rounds: the bound has to hold for every interleaving, and a round is cheap.
            for (var round = 0; round < 200; round++)
            {
                var logger = new RecordingLogger();
                var directives = new ServerDirectives(logger);

                using (var together = new Barrier(threads))
                {
                    var senders = Enumerable.Range(0, threads).Select(thread => Task.Run(() =>
                    {
                        together.SignalAndWait();

                        // Names of this thread alone: more new names arrive at once than there is room for.
                        for (var i = 0; i < namesPerThread; i++)
                            directives.Receive($"name-{thread}-{i}");
                    })).ToArray();

                    Task.WaitAll(senders);
                }

                // Every name is different, so every slot that was taken holds a name: the table
                // is full, and not by one name more.
                Assert.Equal(ServerDirectives.MaxNotedNames, directives.NotedNamesCount);
                Assert.Equal(ServerDirectives.MaxNotedNames, logger.Debugs.Count(d => d.Contains("is not handled")));
                Assert.Equal(threads * namesPerThread, directives.ReceivedCount);
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
        // The .NET Framework handler replaces a hand-set Cookie with its own (empty) cookie
        // container, so the header would be sent on net6.0 and lost on net472.
        [InlineData("Cookie", "route=a")]
        [InlineData("cookie2", "$Version=1")]
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

        /// <summary>
        /// Answers every request with 200 and one <c>X-Hsm-Directive</c> header line per
        /// directive, written to the socket as it is.
        /// </summary>
        private sealed class DirectiveLinesServer : IDisposable
        {
            private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
            private readonly byte[] _answer;
            private readonly Task _loop;

            internal DirectiveLinesServer(params string[] directives)
            {
                var answer = new StringBuilder("HTTP/1.1 200 OK\r\n");

                foreach (var directive in directives)
                    answer.Append("X-Hsm-Directive: ").Append(directive).Append("\r\n");

                answer.Append("Content-Length: 0\r\nConnection: close\r\n\r\n");
                _answer = Encoding.ASCII.GetBytes(answer.ToString());

                _listener.Start();
                Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                _loop = Task.Run(AcceptLoopAsync);
            }

            internal int Port { get; }

            public void Dispose()
            {
                _listener.Stop();
                _loop.Wait(TimeSpan.FromSeconds(5));
            }

            private async Task AcceptLoopAsync()
            {
                while (true)
                {
                    TcpClient connection;

                    try
                    {
                        connection = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        return; // the listener was stopped
                    }

                    using (connection)
                    {
                        try
                        {
                            await AnswerAsync(connection.GetStream()).ConfigureAwait(false);
                        }
                        catch (Exception)
                        {
                            // A client that went away; the test sees it as a failed send.
                        }
                    }
                }
            }

            // Reads one request to its end (the head, then as many body bytes as it announces)
            // and writes the answer.
            private async Task AnswerAsync(NetworkStream stream)
            {
                var request = new MemoryStream();
                var buffer = new byte[4096];
                var headEnd = -1;

                while (headEnd < 0)
                {
                    var read = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                    if (read == 0)
                        return;

                    request.Write(buffer, 0, read);
                    headEnd = Encoding.ASCII.GetString(request.GetBuffer(), 0, (int)request.Length).IndexOf("\r\n\r\n", StringComparison.Ordinal);
                }

                var head = Encoding.ASCII.GetString(request.GetBuffer(), 0, headEnd);
                var bodyLength = 0;

                foreach (var line in head.Split(new[] { "\r\n" }, StringSplitOptions.None))
                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        bodyLength = int.Parse(line.Substring("Content-Length:".Length).Trim());

                var missing = headEnd + 4 + bodyLength - request.Length;

                while (missing > 0)
                {
                    var read = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                    if (read == 0)
                        return;

                    missing -= read;
                }

                await stream.WriteAsync(_answer, 0, _answer.Length).ConfigureAwait(false);
            }
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
