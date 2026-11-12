using HSMServer.Core.Model;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.ServerConfiguration;
using System;
using System.Buffers;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace HSMServer.Core.Tests.SensorApi
{
    // The collector session (#1664, epic #1660, ADR-0017) over a real WebSocket: a real
    // ClientWebSocket against the real Sensor API on a real Kestrel listener, with the real
    // cache underneath. The HTTP routes run in the same host, so "the ack carries the status
    // and body of the HTTP route" is checked against the route itself, not against a copy
    // of its expectations.
    //
    // The wire strings (header names, subprotocol, ops, error texts) are written out here
    // on purpose: the tests pin the contract, not the server's constants.
    public class CollectorSessionTests : MonitoringCoreTestsBase<CollectorSessionFixture>
    {
        private const string Subprotocol = "hsm.sensors.v1";
        private const string ClientName = "session-tests";

        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

        private readonly ITestOutputHelper _output;

        private readonly string _key = TestProductsManager.TestProductKey.Id;
        private readonly string _telemetryPath = $"{TestProductsManager.ProductName}/{TestProductsManager.TestProductKey.DisplayName}/{ClientName}";


        public CollectorSessionTests(CollectorSessionFixture fixture, DatabaseRegisterFixture registerFixture, ITestOutputHelper output)
            : base(fixture, registerFixture)
        {
            _output = output;
        }


        [Fact]
        public async Task Upgrade_WithAValidKey_SelectsTheSubprotocolAndSaysHello()
        {
            await using var host = await StartHost(config =>
            {
                config.MaxFrameBytes = 128 * 1024;
                config.HeartbeatSeconds = 17;
            });

            using var socket = await ConnectAsync(host, _key);

            Assert.Equal(Subprotocol, socket.SubProtocol);

            var hello = await ReceiveAsync(socket);

            Assert.Equal("hello", (string)hello["op"]);
            Assert.Equal(128 * 1024, (int)hello["body"]["maxFrameBytes"]);
            Assert.Equal(17, (int)hello["body"]["heartbeatSeconds"]);
        }

        [Theory]
        [InlineData("none")]
        [InlineData("unknown")]
        [InlineData("malformed")]
        [InlineData("no-send-permission")]
        [InlineData("blocked")]
        public async Task Upgrade_WithoutUsableAccess_IsRefusedWith406(string access)
        {
            await using var host = await StartHost();

            var key = access switch
            {
                "none" => null,
                "unknown" => Guid.NewGuid().ToString(),
                "malformed" => "not-a-key",
                "no-send-permission" => AddKey(KeyPermissions.CanReadSensorData).ToString(),
                "blocked" => Block(AddKey()).ToString(),
                _ => throw new ArgumentOutOfRangeException(nameof(access)),
            };

            Assert.Equal(HttpStatusCode.NotAcceptable, await RefusedUpgradeStatus(host, key, Subprotocol));
            Assert.Equal(0, host.Registry.Count);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("hsm.sensors.v2")]
        public async Task Upgrade_WithoutTheSubprotocol_IsRefusedWith400(string offered)
        {
            await using var host = await StartHost();

            Assert.Equal(HttpStatusCode.BadRequest, await RefusedUpgradeStatus(host, _key, offered));
        }

        [Fact]
        public async Task PlainGet_IsNotASession()
        {
            await using var host = await StartHost();
            using var http = new HttpClient();

            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(host.HttpBase, "session"));
            request.Headers.Add("Key", _key);

            using var response = await http.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task SessionsOff_NoEndpoint_AndTheHttpRoutesStillWork()
        {
            await using var host = await StartHost(config => config.MaxSessions = 0);

            Assert.Equal(HttpStatusCode.NotFound, await RefusedUpgradeStatus(host, _key, Subprotocol));
            Assert.Equal(HttpStatusCode.OK, (await PostAsync(host, "list", $"[{IntValue("off/int", 1)}]", _key)).Status);
        }

        [Fact]
        public async Task List_IsStored_AndAnsweredWithTheStatusAndBodyOfTheHttpRoute()
        {
            await using var host = await StartHost();
            using var socket = await OpenAsync(host);

            // An accepted batch.
            var http = await PostAsync(host, "list", $"[{IntValue("parity/http", 7)}]", _key);

            await SendAsync(socket, $"{{\"id\":1,\"op\":\"list\",\"body\":[{IntValue("parity/session", 7)}]}}");
            var ack = await ReceiveAsync(socket);

            Assert.Equal(HttpStatusCode.OK, http.Status);
            Assert.Equal(1, (long)ack["ack"]);
            Assert.Equal(200, (int)ack["status"]);
            Assert.Null(ack["error"]);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(http.Body), ack["body"]), $"HTTP answered {http.Body}, the session {ack["body"]?.ToJsonString()}");

            await TestWait.UntilAsync(() => _valuesCache.TryGetSensorByPath(TestProductsManager.ProductId, "parity/http", out _) &&
                                            _valuesCache.TryGetSensorByPath(TestProductsManager.ProductId, "parity/session", out _),
                "both batches to reach their sensors");

            // The same batch over both transports with one value the server refuses (its path
            // is deeper than a sensor path may be) next to one it stores.
            var rejectedBody = $"[{IntValue("parity/a/b/c/d/e/f/g/h/i/j", 1)},{IntValue("parity/next-to-rejected", 2)}]";

            var httpRejected = await PostAsync(host, "list", rejectedBody, _key);

            await SendAsync(socket, $"{{\"id\":2,\"op\":\"list\",\"body\":{rejectedBody}}}");
            var rejectedAck = await ReceiveAsync(socket);

            Assert.Equal((int)httpRejected.Status, (int)rejectedAck["status"]);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(httpRejected.Body), rejectedAck["body"]),
                $"HTTP answered {httpRejected.Body}, the session {rejectedAck["body"]?.ToJsonString()}");
            Assert.Equal("Path for the sensor is too long.", (string)rejectedAck["body"]["parity/a/b/c/d/e/f/g/h/i/j"]);
            Assert.Single(rejectedAck["body"].AsObject());
        }

        [Fact]
        public async Task EmptyBatch_Is400OnBothTransports_AndTheSessionDoesNotEchoIt()
        {
            await using var host = await StartHost();
            using var socket = await OpenAsync(host);

            var http = await PostAsync(host, "list", "[]", _key);

            await SendAsync(socket, "{\"id\":3,\"op\":\"list\",\"body\":[]}");
            var ack = await ReceiveAsync(socket);

            Assert.Equal(HttpStatusCode.BadRequest, http.Status);
            Assert.Equal(400, (int)ack["status"]);
            Assert.Equal("empty batch", (string)ack["error"]);
            Assert.Null(ack["body"]);
        }

        [Fact]
        public async Task FailureAck_CarriesAReason_AndNeverTheRequest()
        {
            await using var host = await StartHost();
            using var socket = await OpenAsync(host);

            const string malformedBody = "[{\"Type\":1,\"Value\":\"MARKER-not-a-number\",\"Path\":\"failure/int\"}]";

            // The one place where the ack deliberately differs from the HTTP route. There the
            // model binder throws and the request ends as a 500, which tells a collector to
            // retry a body that can never be accepted. The session answers 400. If this
            // assertion fails because the route learned to answer 400, the difference is
            // gone: update the wire-contract doc.
            Assert.Equal(HttpStatusCode.InternalServerError, (await PostAsync(host, "list", malformedBody, _key)).Status);

            await SendAsync(socket, $"{{\"id\":4,\"op\":\"list\",\"body\":{malformedBody}}}");
            var malformed = await ReceiveTextAsync(socket);

            await SendAsync(socket, "{\"id\":5,\"op\":\"list\"}");
            var missing = await ReceiveAsync(socket);

            var ack = JsonNode.Parse(malformed);

            Assert.Equal(4, (long)ack["ack"]);
            Assert.Equal(400, (int)ack["status"]);
            Assert.Equal("malformed body", (string)ack["error"]);
            Assert.DoesNotContain("MARKER", malformed);

            Assert.Equal(400, (int)missing["status"]);
            Assert.Equal("malformed body", (string)missing["error"]);

            // A value the hand-written version converter cannot read throws something other
            // than a JSON error. It is a malformed body all the same, not the end of the session.
            foreach (var version in new[] { "{\"Type\":8,\"Value\":\"abc\",\"Time\":\"2026-11-10T10:00:00Z\",\"Status\":1,\"Path\":\"failure/version\"}", "{\"Type\":8,\"Value\":\"1.2.3\",\"Path\":\"failure/version\"}" })
            {
                await SendAsync(socket, $"{{\"id\":6,\"op\":\"list\",\"body\":[{version}]}}");
                var refused = await ReceiveAsync(socket);

                Assert.Equal(400, (int)refused["status"]);
                Assert.Equal("malformed body", (string)refused["error"]);
            }

            // The session survives a refused request.
            await SendAsync(socket, "{\"op\":\"ping\"}");
            Assert.Equal("pong", (string)(await ReceiveAsync(socket))["op"]);
        }

        [Fact]
        public async Task Commands_AreApplied_AndAnsweredWithTheStatusAndBodyOfTheHttpRoute()
        {
            await using var host = await StartHost();
            using var socket = await OpenAsync(host);

            var http = await PostAsync(host, "commands", "[{\"Type\":0,\"Path\":\"commands/http\",\"SensorType\":1}]", _key);

            await SendAsync(socket, "{\"id\":6,\"op\":\"commands\",\"body\":[{\"Type\":0,\"Path\":\"commands/session\",\"SensorType\":1}]}");
            var ack = await ReceiveAsync(socket);

            Assert.Equal(HttpStatusCode.OK, http.Status);
            Assert.Equal(200, (int)ack["status"]);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(http.Body), ack["body"]), $"HTTP answered {http.Body}, the session {ack["body"]?.ToJsonString()}");

            Assert.True(_valuesCache.TryGetSensorByPath(TestProductsManager.ProductId, "commands/http", out var overHttp));
            Assert.True(_valuesCache.TryGetSensorByPath(TestProductsManager.ProductId, "commands/session", out var overSession));
            Assert.Equal(overHttp.Type, overSession.Type);

            // The same batch again over both transports, now for a sensor that exists with
            // another type, plus an empty batch: whatever the route answers, the ack answers.
            foreach (var body in new[] { "[{\"Type\":0,\"Path\":\"commands/http\",\"SensorType\":0}]", "[]" })
            {
                var again = await PostAsync(host, "commands", body, _key);

                await SendAsync(socket, $"{{\"id\":7,\"op\":\"commands\",\"body\":{body}}}");
                var againAck = await ReceiveAsync(socket);

                Assert.Equal((int)again.Status, (int)againAck["status"]);
                Assert.True(JsonNode.DeepEquals(JsonNode.Parse(again.Body), againAck["body"]), $"HTTP answered {again.Body}, the session {againAck["body"]?.ToJsonString()}");
            }
        }

        // Sensor move from a collector (#1724): a registration's PreviousPaths moves the old sensor
        // to the registered path — the same over the HTTP route and over the session, which both
        // feed SensorIngestionService.AddOrUpdateSensorAsync.
        [Fact]
        public async Task PreviousPaths_MoveTheOldSensor_OverHttpAndOverTheSession()
        {
            await using var host = await StartHost();
            using var socket = await OpenAsync(host);

            Assert.Equal(HttpStatusCode.OK, (await PostAsync(host, "commands",
                "[{\"Type\":0,\"Path\":\"layout/v1/http\",\"SensorType\":1},{\"Type\":0,\"Path\":\"layout/v1/session\",\"SensorType\":1}]", _key)).Status);
            Assert.True(_valuesCache.TryGetSensorByPath(TestProductsManager.ProductId, "layout/v1/http", out var overHttp));
            Assert.True(_valuesCache.TryGetSensorByPath(TestProductsManager.ProductId, "layout/v1/session", out var overSession));

            var http = await PostAsync(host, "commands",
                "[{\"Type\":0,\"Path\":\"layout/v2/http\",\"SensorType\":1,\"PreviousPaths\":[\"layout/v1/http\"]}]", _key);
            await SendAsync(socket,
                "{\"id\":8,\"op\":\"commands\",\"body\":[{\"Type\":0,\"Path\":\"layout/v2/session\",\"SensorType\":1,\"PreviousPaths\":[\"layout/v1/session\"]}]}");
            var ack = await ReceiveAsync(socket);

            Assert.Equal(HttpStatusCode.OK, http.Status);
            Assert.Equal(200, (int)ack["status"]);

            Assert.True(_valuesCache.TryGetSensorByPath(TestProductsManager.ProductId, "layout/v2/http", out var movedOverHttp));
            Assert.Equal(overHttp.Id, movedOverHttp.Id);
            Assert.True(_valuesCache.TryGetSensorByPath(TestProductsManager.ProductId, "layout/v2/session", out var movedOverSession));
            Assert.Equal(overSession.Id, movedOverSession.Id);
            Assert.False(_valuesCache.TryGetSensorByPath(TestProductsManager.ProductId, "layout/v1/http", out _));
        }

        [Fact]
        public async Task UnknownOp_Is400_AndUnknownEnvelopeFieldsAreIgnored()
        {
            await using var host = await StartHost();
            using var socket = await OpenAsync(host);

            await SendAsync(socket, "{\"id\":8,\"op\":\"teleport\",\"body\":[]}");
            var unknown = await ReceiveAsync(socket);

            await SendAsync(socket, $"{{\"id\":9,\"op\":\"list\",\"since\":\"v9\",\"body\":[{IntValue("unknown/int", 1)}],\"trailer\":{{\"x\":1}}}}");
            var tolerant = await ReceiveAsync(socket);

            Assert.Equal(8, (long)unknown["ack"]);
            Assert.Equal(400, (int)unknown["status"]);
            Assert.Equal("unknown op", (string)unknown["error"]);

            Assert.Equal(9, (long)tolerant["ack"]);
            Assert.Equal(200, (int)tolerant["status"]);
        }

        [Fact]
        public async Task Ping_IsAnsweredWithPong()
        {
            await using var host = await StartHost();
            using var socket = await OpenAsync(host);

            await SendAsync(socket, "{\"op\":\"ping\"}");

            Assert.Equal("{\"op\":\"pong\"}", await ReceiveTextAsync(socket));

            // A collector that numbers every frame it sends: still a heartbeat, not a request.
            await SendAsync(socket, "{\"id\":41,\"op\":\"ping\"}");

            Assert.Equal("{\"op\":\"pong\"}", await ReceiveTextAsync(socket));
        }

        [Theory]
        [InlineData("request-without-id", WebSocketCloseStatus.ProtocolError)]
        [InlineData("not-json", WebSocketCloseStatus.InvalidPayloadData)]
        [InlineData("not-an-object", WebSocketCloseStatus.InvalidPayloadData)]
        [InlineData("binary", WebSocketCloseStatus.InvalidMessageType)]
        [InlineData("too-big", WebSocketCloseStatus.MessageTooBig)]
        public async Task ProtocolViolation_ClosesTheSession(string violation, WebSocketCloseStatus expected)
        {
            await using var host = await StartHost(config => config.MaxFrameBytes = 64 * 1024);
            using var socket = await OpenAsync(host);

            switch (violation)
            {
                case "request-without-id":
                    // Nothing could acknowledge it, so it is refused instead of being stored unanswered.
                    await SendAsync(socket, $"{{\"op\":\"list\",\"body\":[{IntValue("violation/int", 1)}]}}");
                    break;
                case "not-json":
                    await SendAsync(socket, "hello there");
                    break;
                case "not-an-object":
                    await SendAsync(socket, "[1,2,3]");
                    break;
                case "binary":
                    await socket.SendAsync(new byte[] { 1, 2, 3 }, WebSocketMessageType.Binary, true, Deadline());
                    break;
                case "too-big":
                    await SendAsync(socket, $"{{\"id\":1,\"op\":\"list\",\"body\":\"{new string('x', 70 * 1024)}\"}}");
                    break;
            }

            Assert.Equal(expected, await ExpectCloseAsync(socket));

            await TestWait.UntilAsync(() => host.Registry.Count == 0, "the closed session to leave the registry");
            Assert.False(_valuesCache.TryGetSensorByPath(TestProductsManager.ProductId, "violation/int", out _));
        }

        [Fact]
        public async Task SessionCap_RefusesTheNextUpgradeWith503_AndFreesTheSlotOnClose()
        {
            await using var host = await StartHost(config => config.MaxSessions = 1);

            using (var first = await OpenAsync(host))
            {
                Assert.Equal(HttpStatusCode.ServiceUnavailable, await RefusedUpgradeStatus(host, _key, Subprotocol));

                // The refused upgrade did not take the slot with it.
                Assert.Equal(1, host.Registry.Count);

                await first.CloseAsync(WebSocketCloseStatus.NormalClosure, null, Deadline());
            }

            await TestWait.UntilAsync(() => host.Registry.Count == 0, "the closed session to leave the registry");

            using var second = await OpenAsync(host);

            Assert.Equal(1, host.Registry.Count);
        }

        [Fact]
        public async Task AddressLimit_RefusesTheNextUpgradeWith429_AndFreesTheSlotOnClose()
        {
            // Far below the server-wide cap: the limit that applies is the one per address.
            await using var host = await StartHost(config => config.MaxSessionsPerAddress = 2);

            using var first = await OpenAsync(host);
            using var second = await OpenAsync(host);

            Assert.Equal(HttpStatusCode.TooManyRequests, await RefusedUpgradeStatus(host, _key, Subprotocol));
            Assert.Equal(2, host.Registry.Count);

            await first.CloseAsync(WebSocketCloseStatus.NormalClosure, null, Deadline());
            await TestWait.UntilAsync(() => host.Registry.Count == 1, "the closed session to leave the registry");

            using var third = await OpenAsync(host);

            Assert.Equal(2, host.Registry.Count);
        }

        [Theory]
        [InlineData("203.0.113.7", "203.0.113.7")]
        [InlineData("2001:db8:1:2:aaaa:bbbb:cccc:dddd", "2001:db8:1:2::/64")]
        [InlineData("2001:db8:1:2::1", "2001:db8:1:2::/64")]
        [InlineData("2001:db8:1:3::1", "2001:db8:1:3::/64")]
        public void AddressLimit_CountsAnIPv6HostByItsPrefix(string address, string countedAs)
        {
            // An IPv6 host can choose any source address in its /64; counted one by one it
            // would never reach the limit.
            Assert.Equal(countedAs, HSMServer.SensorApi.Sessions.CollectorSessionRegistry.AddressKey(address));
        }

        [Fact]
        public async Task AddressLimit_OfZero_IsNoLimit()
        {
            await using var host = await StartHost(config => config.MaxSessionsPerAddress = 0);

            var sockets = new ClientWebSocket[CollectorSessionsConfig.DefaultMaxSessionsPerAddress + 2];

            for (var i = 0; i < sockets.Length; i++)
                sockets[i] = await OpenAsync(host);

            Assert.Equal(sockets.Length, host.Registry.Count);

            foreach (var socket in sockets)
                socket.Dispose();
        }

        [Fact]
        public async Task Silence_EndsTheSessionAfterThreeHeartbeats_AndEveryFrameStartsTheCountAgain()
        {
            await using var host = await StartHost(config => config.HeartbeatSeconds = 30);
            using var socket = await OpenAsync(host);

            // The session's silence watch runs on the host's manual clock.
            var watch = Assert.Single(host.Time.Timers);

            await TestWait.UntilAsync(() => watch.IsArmed, "the watch to run while the server waits for a frame");
            Assert.Equal(TimeSpan.FromSeconds(90), watch.DueTime);

            // A ping is a frame: the count starts again.
            var armsBefore = watch.Arms;

            await SendAsync(socket, "{\"op\":\"ping\"}");
            Assert.Equal("pong", (string)(await ReceiveAsync(socket))["op"]);

            await TestWait.UntilAsync(() => watch.Arms > armsBefore && watch.IsArmed, "the watch to start again after a frame");
            Assert.Equal(1, host.Registry.Count);

            // Three heartbeats pass with nothing from the collector. The server does not wait
            // for a close handshake with a collector it considers gone: the connection ends.
            watch.Fire();

            await TestWait.UntilAsync(() => host.Registry.Count == 0, "the silent session to be dropped");
            Assert.True(await EndsAsync(socket), "the collector's end of a dropped session must see it end");
        }

        [Theory]
        [InlineData("blocked")]
        [InlineData("removed")]
        public async Task KeyLostWhileConnected_ClosesWith1008(string loss)
        {
            await using var host = await StartHost();

            var key = AddKey();

            using var socket = await OpenAsync(host, key.ToString());

            await SendAsync(socket, $"{{\"id\":1,\"op\":\"list\",\"body\":[{IntValue("revoked/before", 1)}]}}");
            Assert.Equal(200, (int)(await ReceiveAsync(socket))["status"]);

            if (loss == "blocked")
                Block(key);
            else
                _valuesCache.RemoveAccessKey(key);

            await SendAsync(socket, $"{{\"id\":2,\"op\":\"list\",\"body\":[{IntValue("revoked/after", 1)}]}}");

            Assert.Equal(WebSocketCloseStatus.PolicyViolation, await ExpectCloseAsync(socket));
            Assert.False(_valuesCache.TryGetSensorByPath(TestProductsManager.ProductId, "revoked/after", out _));
        }

        [Fact]
        public async Task FrameInPieces_KeepsTheSilenceLimitAway_AndIsHandledWhenWhole()
        {
            await using var host = await StartHost();
            using var socket = await OpenAsync(host);

            var watch = Assert.Single(host.Time.Timers);

            await TestWait.UntilAsync(() => watch.IsArmed, "the watch to run while the server waits for a frame");
            var armsBefore = watch.Arms;

            // The first piece of a frame: bytes arrived, so the count starts again although
            // no frame is complete.
            await socket.SendAsync(Encoding.UTF8.GetBytes("{\"op\":"), WebSocketMessageType.Text, endOfMessage: false, Deadline());
            await TestWait.UntilAsync(() => watch.Arms > armsBefore, "a piece of a frame to start the silence count again");

            await socket.SendAsync(Encoding.UTF8.GetBytes("\"ping\"}"), WebSocketMessageType.Text, endOfMessage: true, Deadline());

            Assert.Equal("pong", (string)(await ReceiveAsync(socket))["op"]);
        }

        [Fact]
        public async Task PlainRequest_WithAnotherMethod_IsNotRouted()
        {
            await using var host = await StartHost();

            var post = await PostAsync(host, "session", "[]", _key);

            Assert.Equal(HttpStatusCode.MethodNotAllowed, post.Status);
        }

        [Fact]
        public async Task KeyBlocked_EndsASessionThatOnlyPings()
        {
            await using var host = await StartHost();

            var key = AddKey();

            using var socket = await OpenAsync(host, key.ToString());

            await SendAsync(socket, "{\"op\":\"ping\"}");
            Assert.Equal("pong", (string)(await ReceiveAsync(socket))["op"]);

            Block(key);

            // No data frame is needed to notice: the next heartbeat ends it.
            await SendAsync(socket, "{\"op\":\"ping\"}");

            Assert.Equal(WebSocketCloseStatus.PolicyViolation, await ExpectCloseAsync(socket));
            await TestWait.UntilAsync(() => host.Registry.Count == 0, "the closed session to leave the registry");
        }

        [Fact]
        public async Task Registry_ListsTheSessionWithWhatTheCollectorSaidAboutItself()
        {
            await using var host = await StartHost();

            var before = DateTime.UtcNow;

            using var socket = await OpenAsync(host, _key, options =>
            {
                options.SetRequestHeader("X-Collector-Kind", "native");
                options.SetRequestHeader("X-Collector-Version", "0.11.4");
                options.SetRequestHeader("X-Agent-Version", "0.5.49");
            });

            var session = Assert.Single(host.Registry.GetSessions());

            Assert.Equal(TestProductsManager.ProductId, session.ProductId);
            Assert.Equal(TestProductsManager.ProductName, session.ProductName);
            Assert.Equal(Guid.Parse(_key), session.KeyId);
            Assert.Equal(TestProductsManager.TestProductKey.DisplayName, session.KeyName);
            Assert.Equal(ClientName, session.ClientName);
            Assert.Equal("native", session.CollectorKind);
            Assert.Equal("0.11.4", session.CollectorVersion);
            Assert.Equal("0.5.49", session.AgentVersion);
            Assert.Equal("127.0.0.1", session.RemoteIP);
            Assert.InRange(session.ConnectedAtUtc, before.AddSeconds(-1), DateTime.UtcNow.AddSeconds(1));

            await Task.Delay(50);
            await SendAsync(socket, "{\"op\":\"ping\"}");
            await ReceiveAsync(socket);

            Assert.True(Assert.Single(host.Registry.GetSessions()).LastFrameAtUtc > session.LastFrameAtUtc, "an inbound frame must move last-frame-at");

            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, Deadline());
            await TestWait.UntilAsync(() => host.Registry.Count == 0, "the closed session to leave the registry");
        }

        [Fact]
        public async Task ServerShutdown_ClosesSessionsWith1001_AndDoesNotWaitForThem()
        {
            await using var host = await StartHost();
            using var socket = await OpenAsync(host);

            // On another thread: the host closes its sessions inside StopAsync, and the
            // collector's end has to be reading meanwhile to answer the close.
            var stopping = Stopwatch.StartNew();
            var stop = Task.Run(host.StopAsync);

            Assert.Equal(WebSocketCloseStatus.EndpointUnavailable, await ExpectCloseAsync(socket));

            await stop.WaitAsync(Patience);

            Assert.True(stopping.Elapsed < TimeSpan.FromSeconds(8), $"shutdown took {stopping.Elapsed} with one open session");
            Assert.Equal(0, host.Registry.Count);
        }

        [Fact]
        public async Task ShutdownDuringAStore_AcknowledgesTheBatchBeforeClosing()
        {
            var gated = GatedCache.Wrap(_valuesCache);

            await using var host = await SensorApiTestHost.StartAsync(gated.Cache, new CollectorSessionsConfig());
            using var socket = await OpenAsync(host);

            // A batch is inside the store when the server starts shutting down.
            gated.HoldTheNextStore();
            await SendAsync(socket, $"{{\"id\":1,\"op\":\"list\",\"body\":[{IntValue("shutdown/stored", 1)}]}}");
            await gated.Entered.Task.WaitAsync(Patience);

            var stop = Task.Run(host.StopAsync);

            // The shutdown has reached the session and found it busy: its close now waits.
            await TestWait.UntilAsync(() => host.Registry.AnyDeferredClose, "the shutdown to ask the busy session to close");
            gated.Release();

            // The batch was stored, so its ack must still arrive, and only then the close.
            var ack = await ReceiveAsync(socket);

            Assert.Equal(1, (long)ack["ack"]);
            Assert.Equal(200, (int)ack["status"]);
            Assert.Equal(WebSocketCloseStatus.EndpointUnavailable, await ExpectCloseAsync(socket));

            await stop.WaitAsync(Patience);

            Assert.True(_valuesCache.TryGetSensorByPath(TestProductsManager.ProductId, "shutdown/stored", out _));
        }

        [Fact]
        public async Task CollectorVanishing_ReleasesItsSession()
        {
            await using var host = await StartHost();

            var socket = await OpenAsync(host);

            Assert.Equal(1, host.Registry.Count);

            // No close handshake: the process on the other end is simply gone.
            socket.Abort();
            socket.Dispose();

            await TestWait.UntilAsync(() => host.Registry.Count == 0, "the vanished collector's session to leave the registry");
        }

        [Fact]
        public async Task SessionTraffic_IsCountedLikeHttpTraffic()
        {
            await using var host = await StartHost();
            using var socket = await OpenAsync(host);

            await PostAsync(host, "list", $"[{IntValue("telemetry/a", 1)},{IntValue("telemetry/b", 2)}]", _key);

            Assert.Equal(2, host.Telemetry.Sum("values", _telemetryPath));

            var frame = $"{{\"id\":1,\"op\":\"list\",\"body\":[{IntValue("telemetry/c", 1)},{IntValue("telemetry/d", 2)},{IntValue("telemetry/e", 3)}]}}";
            var requestsBefore = host.Telemetry.Records.Count(record => record.Kind == "request" && record.Path == _telemetryPath);

            await SendAsync(socket, frame);
            await ReceiveAsync(socket);

            Assert.Equal(5, host.Telemetry.Sum("values", _telemetryPath));

            // One request frame is one request with its size, as an HTTP request is.
            var requests = host.Telemetry.Records.Where(record => record.Kind == "request" && record.Path == _telemetryPath).ToList();

            Assert.Equal(requestsBefore + 1, requests.Count);
            Assert.Equal(Encoding.UTF8.GetByteCount(frame), requests[^1].Amount);
        }

        [Fact]
        public async Task HttpRoutes_BehaveAsBeforeTheIngestionServiceWasExtracted()
        {
            await using var host = await StartHost();

            // A typed single-value route stores its value.
            var typed = await PostAsync(host, "int", IntValue("legacy/int", 5), _key);

            Assert.Equal(HttpStatusCode.OK, typed.Status);
            await TestWait.UntilAsync(() => _valuesCache.TryGetSensorByPath(TestProductsManager.ProductId, "legacy/int", out _), "the typed value to reach its sensor");

            // A key that may not send is refused with 406 and nothing is stored.
            var readOnly = AddKey(KeyPermissions.CanReadSensorData).ToString();
            var refused = await PostAsync(host, "list", $"[{IntValue("legacy/refused", 5)}]", readOnly);

            Assert.Equal(HttpStatusCode.NotAcceptable, refused.Status);
            Assert.False(_valuesCache.TryGetSensorByPath(TestProductsManager.ProductId, "legacy/refused", out _));

            // The HTTP routes say nothing about sessions: a collector on a session does not
            // learn of it from HTTP, and one on HTTP is not nudged towards it.
            using (var http = new HttpClient())
            using (var request = new HttpRequestMessage(HttpMethod.Get, new Uri(host.HttpBase, "testConnection")))
            {
                request.Headers.Add("Key", _key);

                using var response = await http.SendAsync(request, Deadline());

                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.DoesNotContain(response.Headers, header => header.Key.Contains("Session", StringComparison.OrdinalIgnoreCase));
            }

            // A registration through the single-command route.
            var registered = await PostAsync(host, "addOrUpdate", "{\"Type\":0,\"Path\":\"legacy/registered\",\"SensorType\":2}", _key);

            Assert.Equal(HttpStatusCode.OK, registered.Status);
            Assert.True(_valuesCache.TryGetSensorByPath(TestProductsManager.ProductId, "legacy/registered", out _));
        }

        // The number behind the default session cap: what one idle session holds on the
        // server. The clients are raw sockets that only do the upgrade, so nearly all of the
        // measured growth is the server's. Printed for the record; the assertion is only a
        // guard against the cost changing by an order of magnitude.
        [Fact]
        public async Task IdleSession_CostsTensOfKilobytes()
        {
            const int sessions = 400;

            await using var host = await StartHost(config =>
            {
                config.MaxSessions = sessions + 50;
                config.MaxSessionsPerAddress = 0; // every session of this test comes from one address
            });

            // Warm up everything that is allocated once.
            foreach (var warm in await OpenRawSessionsAsync(host, 20))
                warm.Dispose();

            await TestWait.UntilAsync(() => host.Registry.Count == 0, "the warm-up sessions to close");

            var before = SettledMemory();
            var open = await OpenRawSessionsAsync(host, sessions);

            Assert.Equal(sessions, host.Registry.Count);

            var perSession = (SettledMemory() - before) / sessions;

            _output.WriteLine($"An idle collector session holds about {perSession / 1024.0:F1} KiB of managed memory ({sessions} sessions measured).");

            foreach (var socket in open)
                socket.Dispose();

            Assert.InRange(perSession, 1, 256 * 1024);
        }


        private Task<SensorApiTestHost> StartHost(Action<CollectorSessionsConfig> configure = null)
        {
            var config = new CollectorSessionsConfig();

            configure?.Invoke(config);

            return SensorApiTestHost.StartAsync(_valuesCache, config);
        }

        private Guid AddKey(KeyPermissions? permissions = null)
        {
            var entity = permissions is null
                ? EntitiesFactory.BuildAccessKeyEntity(productId: TestProductsManager.TestProduct.Id)
                : EntitiesFactory.BuildAccessKeyEntity(productId: TestProductsManager.TestProduct.Id, permissions: permissions.Value);

            return _valuesCache.AddAccessKey(new AccessKeyModel(entity)).Id;
        }

        private Guid Block(Guid key)
        {
            _valuesCache.UpdateAccessKeyState(key, KeyState.Blocked);

            return key;
        }


        private static CancellationToken Deadline() => new CancellationTokenSource(Patience).Token;

        private static string IntValue(string path, int value) =>
            $"{{\"Type\":1,\"Value\":{value},\"Comment\":null,\"Time\":\"{DateTime.UtcNow:O}\",\"Status\":1,\"Key\":null,\"Path\":\"{path}\"}}";


        private static ClientWebSocket NewSocket(string key, string subprotocol, Action<ClientWebSocketOptions> configure = null)
        {
            var socket = new ClientWebSocket();

            socket.Options.CollectHttpResponseDetails = true;
            socket.Options.SetRequestHeader("ClientName", ClientName);

            if (key is not null)
                socket.Options.SetRequestHeader("Key", key);

            if (subprotocol is not null)
                socket.Options.AddSubProtocol(subprotocol);

            configure?.Invoke(socket.Options);

            return socket;
        }

        private static async Task<ClientWebSocket> ConnectAsync(SensorApiTestHost host, string key, Action<ClientWebSocketOptions> configure = null)
        {
            var socket = NewSocket(key, Subprotocol, configure);

            await socket.ConnectAsync(host.SessionUri, Deadline());

            return socket;
        }

        // Connected and past the hello frame.
        private async Task<ClientWebSocket> OpenAsync(SensorApiTestHost host, string key = null, Action<ClientWebSocketOptions> configure = null)
        {
            var socket = await ConnectAsync(host, key ?? _key, configure);

            Assert.Equal("hello", (string)(await ReceiveAsync(socket))["op"]);

            return socket;
        }

        private static async Task<HttpStatusCode> RefusedUpgradeStatus(SensorApiTestHost host, string key, string subprotocol)
        {
            using var socket = NewSocket(key, subprotocol);

            await Assert.ThrowsAsync<WebSocketException>(() => socket.ConnectAsync(host.SessionUri, Deadline()));

            return socket.HttpStatusCode;
        }

        private static Task SendAsync(ClientWebSocket socket, string frame) =>
            socket.SendAsync(Encoding.UTF8.GetBytes(frame), WebSocketMessageType.Text, true, Deadline());

        private static async Task<string> ReceiveTextAsync(ClientWebSocket socket)
        {
            var message = new ArrayBufferWriter<byte>();
            var deadline = Deadline();

            while (true)
            {
                var result = await socket.ReceiveAsync(message.GetMemory(4096), deadline);

                Assert.True(result.MessageType == WebSocketMessageType.Text, $"expected a text frame, the session answered {result.MessageType} ({socket.CloseStatus}: {socket.CloseStatusDescription})");

                message.Advance(result.Count);

                if (result.EndOfMessage)
                    return Encoding.UTF8.GetString(message.WrittenSpan);
            }
        }

        private static async Task<JsonNode> ReceiveAsync(ClientWebSocket socket) => JsonNode.Parse(await ReceiveTextAsync(socket));

        // Reads until the server's close frame and completes the handshake.
        private static async Task<WebSocketCloseStatus?> ExpectCloseAsync(ClientWebSocket socket)
        {
            var buffer = new byte[4096];
            var deadline = Deadline();

            while (true)
            {
                var result = await socket.ReceiveAsync(buffer.AsMemory(), deadline);

                if (result.MessageType != WebSocketMessageType.Close)
                    continue;

                if (socket.State == WebSocketState.CloseReceived)
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, deadline);

                return socket.CloseStatus;
            }
        }

        // True when the connection ends, with or without a close frame.
        private static async Task<bool> EndsAsync(ClientWebSocket socket)
        {
            var buffer = new byte[1024];

            try
            {
                while (true)
                {
                    var result = await socket.ReceiveAsync(buffer.AsMemory(), Deadline());

                    if (result.MessageType == WebSocketMessageType.Close)
                        return true;
                }
            }
            catch (WebSocketException)
            {
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        private static async Task<(HttpStatusCode Status, string Body)> PostAsync(SensorApiTestHost host, string route, string body, string key)
        {
            using var http = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(host.HttpBase, route))
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };

            request.Headers.Add("ClientName", ClientName);

            if (key is not null)
                request.Headers.Add("Key", key);

            using var response = await http.SendAsync(request, Deadline());

            return (response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        // Sessions opened by hand over plain sockets: the upgrade request, then the bytes up
        // to the hello frame. Nothing of a WebSocket client is kept per connection.
        private async Task<Socket[]> OpenRawSessionsAsync(SensorApiTestHost host, int count)
        {
            var request = Encoding.ASCII.GetBytes(
                $"GET /api/sensors/session HTTP/1.1\r\nHost: 127.0.0.1:{host.SessionUri.Port}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
                $"Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\nSec-WebSocket-Version: 13\r\nSec-WebSocket-Protocol: {Subprotocol}\r\n" +
                $"Key: {_key}\r\nClientName: {ClientName}\r\n\r\n");

            var sockets = new Socket[count];
            var buffer = new byte[2048];

            for (var i = 0; i < count; i++)
            {
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                await socket.ConnectAsync(IPAddress.Loopback, host.SessionUri.Port);
                await socket.SendAsync(request);

                var received = new StringBuilder();

                while (!received.ToString().Contains("\"hello\""))
                {
                    var read = await socket.ReceiveAsync(buffer, SocketFlags.None, Deadline());

                    Assert.True(read > 0, $"the server closed raw session {i} during the upgrade: {received}");
                    received.Append(Encoding.ASCII.GetString(buffer, 0, read));
                }

                sockets[i] = socket;
            }

            await TestWait.UntilAsync(() => host.Registry.Count >= count, "the raw sessions to be registered");

            return sockets;
        }

        private static long SettledMemory()
        {
            for (var i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            return GC.GetTotalMemory(forceFullCollection: true);
        }
    }
}
