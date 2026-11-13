using HSMCommon.Model;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.ServerConfiguration;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.SensorApi
{
    public sealed class RegistrationCacheFaultFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(RegistrationCacheFaultTests);
    }


    // What the registration routes answer when the fault is not theirs but the cache's or the
    // store's underneath (#1756). Before it, such a fault never reached the route: the cache
    // logged it and answered as applied, so a collector took the registration as delivered
    // and the sensor stayed without its description, unit, TTL and alerts until the host of
    // the collector restarted.
    //
    // The real Sensor API over the real cache; what fails is the store under the cache
    // (FailingDatabaseCore, once for a sensor a test names), or the lookup of the key
    // (FaultingCache).
    public class RegistrationCacheFaultTests : MonitoringCoreTestsBase<RegistrationCacheFaultFixture>
    {
        private const string ClientName = "registration-cache-fault-tests";
        private const string KeyRequired = "An access key is required";

        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

        // Names of sensors whose row the store refuses to write, once each.
        private readonly ConcurrentDictionary<string, byte> _failCreationOnce = new();
        private readonly ConcurrentDictionary<string, byte> _failSaveOnce = new();

        private readonly string _key = TestProductsManager.TestProductKey.Id;


        public RegistrationCacheFaultTests(RegistrationCacheFaultFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture) { }


        // Called by the base constructor. A new sensor is written with AddSensor first, so
        // the creation list is asked there; the save list only when a row is rewritten.
        protected override IDatabaseCore WrapDatabase(IDatabaseCore inner) =>
            new FailingDatabaseCore(inner, _ => false)
            {
                ShouldFailSensorAdd = entity => _failCreationOnce.TryRemove(entity.DisplayName, out _),
                ShouldFailSensorUpdate = entity => _failSaveOnce.TryRemove(entity.DisplayName, out _),
            };


        [Fact]
        public async Task StoreFailsOnce_WhileASensorIsCreated_Is500OnBothTransports_AndTheResendRegistersIt()
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig());
            using var socket = await OpenSessionAsync(host);

            static string Package(string transport) =>
                $"[{Registration($"created/{transport}/before")},{Registration($"created/{transport}/failed-{transport}", "from the resend")},{Registration($"created/{transport}/after")}]";

            _failCreationOnce["failed-http"] = 0;
            _failCreationOnce["failed-session"] = 0;

            var http = await PostAsync(host, "commands", Package("http"), _key);
            var ack = await SendAsync(socket, 1, "commands", Package("session"));

            Assert.Equal(HttpStatusCode.InternalServerError, http.Status);
            Assert.Equal(500, (int)ack["status"]);
            Assert.Equal("failed to apply the commands", (string)ack["error"]);

            foreach (var transport in new[] { "http", "session" })
            {
                // The rest of the package is applied, as for any fault of one command.
                Assert.True(Registered($"created/{transport}/before"), $"{transport}: the command before the failed one");
                Assert.False(Registered($"created/{transport}/failed-{transport}"), $"{transport}: the command the store failed on");
                Assert.True(Registered($"created/{transport}/after"), $"{transport}: the command after the failed one");
            }

            // The store works again (it failed once), and the collector sends the package again.
            var httpResend = await PostAsync(host, "commands", Package("http"), _key);
            var resent = await SendAsync(socket, 2, "commands", Package("session"));

            Assert.Equal(HttpStatusCode.OK, httpResend.Status);
            Assert.Equal(200, (int)resent["status"]);

            foreach (var transport in new[] { "http", "session" })
            {
                Assert.True(_valuesCache.TryGetSensorByPath(TestProductsManager.ProductId, $"created/{transport}/failed-{transport}", out var sensor), $"{transport}: the resend");
                Assert.Equal("from the resend", sensor.Description);
                Assert.Equal("from the resend", StoredDescription($"failed-{transport}"));
            }
        }

        [Fact]
        public async Task StoreFailsOnce_WhileTheSettingsAreSaved_Is500OnBothTransports_AndTheResendSavesThem()
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig());
            using var socket = await OpenSessionAsync(host);

            // The sensors exist, registered without a description.
            Assert.Equal(HttpStatusCode.OK, (await PostAsync(host, "commands", $"[{Registration("saved/http/kept-http")},{Registration("saved/session/kept-session")}]", _key)).Status);

            _failSaveOnce["kept-http"] = 0;
            _failSaveOnce["kept-session"] = 0;

            var http = await PostAsync(host, "commands", $"[{Registration("saved/http/kept-http", "a new description")}]", _key);
            var ack = await SendAsync(socket, 1, "commands", $"[{Registration("saved/session/kept-session", "a new description")}]");

            Assert.Equal(HttpStatusCode.InternalServerError, http.Status);
            Assert.Equal(500, (int)ack["status"]);

            // Nothing of it reached the store: after a restart the description would be gone.
            Assert.NotEqual("a new description", StoredDescription("kept-http"));
            Assert.NotEqual("a new description", StoredDescription("kept-session"));

            var httpResend = await PostAsync(host, "commands", $"[{Registration("saved/http/kept-http", "a new description")}]", _key);
            var resent = await SendAsync(socket, 2, "commands", $"[{Registration("saved/session/kept-session", "a new description")}]");

            Assert.Equal(HttpStatusCode.OK, httpResend.Status);
            Assert.Equal(200, (int)resent["status"]);
            Assert.Equal("a new description", StoredDescription("kept-http"));
            Assert.Equal("a new description", StoredDescription("kept-session"));
        }

        [Fact]
        public async Task StoreFailsOnce_OnTheSingleRegistrationRoute_Is500_AndTheResendRegisters()
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig());

            _failCreationOnce["failed-single"] = 0;

            var answer = await PostAsync(host, "addOrUpdate", Registration("single/failed-single", "from the resend"), _key);

            Assert.Equal(HttpStatusCode.InternalServerError, answer.Status);
            Assert.False(Registered("single/failed-single"));

            var resend = await PostAsync(host, "addOrUpdate", Registration("single/failed-single", "from the resend"), _key);

            Assert.Equal(HttpStatusCode.OK, resend.Status);
            Assert.Equal("from the resend", StoredDescription("failed-single"));
        }

        // For a registration an error of the update queue is always an exception caught on the
        // way, so it is a fault. Here the wait for the queue is cancelled before it starts.
        [Fact]
        public async Task AnErrorOfTheQueue_IsAFaultOfTheRegistration()
        {
            using var cancelled = new CancellationTokenSource();

            cancelled.Cancel();

            var registration = new SensorAddOrUpdateRequest(TestProductsManager.ProductId, "queue/cancelled")
            {
                Update = new SensorUpdate { Id = Guid.Empty },
                Type = SensorType.Integer,
                TypeIsExplicit = true,
            };

            var result = await _valuesCache.AddOrUpdateSensorAsync(registration, cancelled.Token);

            Assert.False(result.IsOk);
            Assert.True(result.IsFault);
            Assert.False(Registered("queue/cancelled"));
        }

        // The other side: a failure that is not known to pass on a repeat keeps the answer it
        // had. Here a subscriber of the cache throws while the sensor is created (not the
        // store): the creation is rolled back and logged, and the route answers as before.
        // Were it a 500, a failure that repeats would be sent again by every collector on
        // every cycle.
        [Fact]
        public async Task AFailureThatIsNotTheStores_IsAnsweredAsBefore()
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig());

            void Throwing(BaseSensorModel sensor, ActionType action)
            {
                if (sensor.DisplayName == "not-the-store" && action == ActionType.Add)
                    throw new InvalidOperationException("a subscriber failed");
            }

            _valuesCache.ChangeSensorEvent += Throwing;

            try
            {
                var answer = await PostAsync(host, "commands", $"[{Registration("unknown/not-the-store")}]", _key);

                Assert.Equal(HttpStatusCode.OK, answer.Status);
                Assert.Empty(JsonNode.Parse(answer.Body).AsObject());
                Assert.False(Registered("unknown/not-the-store"));
            }
            finally
            {
                _valuesCache.ChangeSensorEvent -= Throwing;
            }
        }

        // What the same failure of the store looks like to the callers of the cache that are
        // not the registration routes: what it looked like before.
        [Fact]
        public async Task TheSameStoreFailure_OnTheOtherCallersOfTheCache_IsAnsweredAsBefore()
        {
            // A value for a sensor that does not exist creates it; the store fails on its row.
            _failCreationOnce["value-created"] = 0;

            var value = SensorValuesFactory.BuildSensorValue(SensorType.Integer, "others/value-created", DateTime.UtcNow);
            var stored = await _valuesCache.AddSensorValueAsync(Guid.Parse(_key), TestProductsManager.ProductId, value);

            Assert.True(stored.IsOk);
            Assert.False(stored.IsFault);
            Assert.False(Registered("others/value-created"));

            // A settings update of an existing sensor, as the web UI sends it; the store fails on the save.
            var registration = new SensorAddOrUpdateRequest(TestProductsManager.ProductId, "others/ui-updated")
            {
                Update = new SensorUpdate { Id = Guid.Empty },
                Type = SensorType.Integer,
                TypeIsExplicit = true,
            };

            Assert.True((await _valuesCache.AddOrUpdateSensorAsync(registration)).IsOk);
            Assert.True(_valuesCache.TryGetSensorByPath(TestProductsManager.ProductId, "others/ui-updated", out var sensor));

            _failSaveOnce["ui-updated"] = 0;

            var updated = await _valuesCache.UpdateSensorAsync(new SensorUpdate { Id = sensor.Id, Description = "from the UI" });

            Assert.True(updated.IsOk);
            Assert.False(updated.IsFault);
        }

        // A path with no part in it names no sensor. The cache fails on it with an exception,
        // which is a fault there; the request is at fault, so it stays a refusal.
        [Theory]
        [InlineData("")]
        [InlineData("/")]
        public async Task ARegistrationWithAnEmptyPath_IsStillARefusal(string path)
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig());

            var batch = await PostAsync(host, "commands", $"[{Registration(path)},{Registration("empty-path/next-to-it")}]", _key);

            Assert.Equal(HttpStatusCode.OK, batch.Status);

            var errors = JsonNode.Parse(batch.Body).AsObject();

            Assert.Single(errors);
            Assert.Equal("The registration cannot be converted: it has no path", (string)errors[path]);
            Assert.True(Registered("empty-path/next-to-it"));

            var single = await PostAsync(host, "addOrUpdate", Registration(path), _key);

            Assert.Equal(HttpStatusCode.NotAcceptable, single.Status);
            Assert.Equal("The registration cannot be converted: it has no path", single.Body);
        }

        // A request with no key at all (neither the header nor the body) is the request's
        // fault: the access error, and nothing is applied. It was a 500 on the registration
        // routes. The value routes answer it as before.
        [Fact]
        public async Task ARequestWithNoKeyAtAll_Is406OnTheRegistrationRoutes_AndTheValueRoutesAnswerAsBefore()
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig());

            var batch = await PostAsync(host, "commands", $"[{Registration("no-key/batch/first")},{Registration("no-key/batch/second")}]", key: null);

            Assert.Equal(HttpStatusCode.NotAcceptable, batch.Status);
            Assert.Equal(KeyRequired, batch.Body);
            Assert.False(Registered("no-key/batch/first"));
            Assert.False(Registered("no-key/batch/second"));

            var single = await PostAsync(host, "addOrUpdate", Registration("no-key/single"), key: null);

            Assert.Equal(HttpStatusCode.NotAcceptable, single.Status);
            Assert.Equal(KeyRequired, single.Body);
            Assert.False(Registered("no-key/single"));

            // Not changed by this: a value batch with no key at all.
            var values = await PostAsync(host, "list", "[{\"Type\":1,\"Value\":1,\"Path\":\"no-key/value\"}]", key: null);

            Assert.Equal(HttpStatusCode.BadRequest, values.Status);
        }

        // One command of a batch without a key, next to one whose key (in its body) the server
        // refuses: each is refused under its own path, as key refusals in a batch are.
        [Fact]
        public async Task ACommandWithoutAKey_NextToARefusedOne_IsRefusedUnderItsPath()
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig());

            var unknownKey = Guid.NewGuid().ToString();

            var answer = await PostAsync(host, "commands",
                $"[{{\"Type\":0,\"Path\":\"mixed/refused-key\",\"SensorType\":1,\"Key\":\"{unknownKey}\"}},{Registration("mixed/no-key")}]", key: null);

            Assert.Equal(HttpStatusCode.OK, answer.Status);

            var errors = JsonNode.Parse(answer.Body).AsObject();

            Assert.Equal(2, errors.Count);
            Assert.Equal("Key is invalid.", (string)errors["mixed/refused-key"]);
            Assert.Equal(KeyRequired, (string)errors["mixed/no-key"]);
        }

        // The server fails while it looks the key up: not a rejected key. A collector takes the
        // 406 of a rejected key on a registration as final, so the registration routes answer
        // 500; the value routes answer as before.
        [Fact]
        public async Task KeyLookupFails_Is500OnTheRegistrationRoutes_AndTheValueRoutesAnswerAsBefore()
        {
            var faulting = FaultingCache.Wrap(_valuesCache);

            await using var host = await SensorApiTestHost.StartAsync(faulting.Cache, new CollectorSessionsConfig());

            faulting.FaultOnKeyLookup();

            var batch = await PostAsync(host, "commands", $"[{Registration("lookup/batch")}]", _key);
            var single = await PostAsync(host, "addOrUpdate", Registration("lookup/single"), _key);
            var values = await PostAsync(host, "list", "[{\"Type\":1,\"Value\":1,\"Path\":\"lookup/value\"}]", _key);

            Assert.Equal(HttpStatusCode.InternalServerError, batch.Status);
            Assert.Equal(HttpStatusCode.InternalServerError, single.Status);
            Assert.Equal(HttpStatusCode.NotAcceptable, values.Status);
            Assert.False(Registered("lookup/batch"));
            Assert.False(Registered("lookup/single"));

            // The fault has passed, and the collector sends the registrations again.
            faulting.Recover();

            Assert.Equal(HttpStatusCode.OK, (await PostAsync(host, "commands", $"[{Registration("lookup/batch")}]", _key)).Status);
            Assert.Equal(HttpStatusCode.OK, (await PostAsync(host, "addOrUpdate", Registration("lookup/single"), _key)).Status);
            Assert.True(Registered("lookup/batch"));
            Assert.True(Registered("lookup/single"));
        }


        private bool Registered(string path) => _valuesCache.TryGetSensorByPath(TestProductsManager.ProductId, path, out _);

        // What the store holds for the sensor with this name: what a restart would load.
        private string StoredDescription(string sensorName) =>
            _databaseCoreManager.DatabaseCore.GetAllSensors().Single(sensor => sensor.DisplayName == sensorName).Description;

        private static string Registration(string path, string description = null) =>
            $"{{\"Type\":0,\"Path\":\"{path}\",\"SensorType\":1{(description is null ? string.Empty : $",\"Description\":\"{description}\"")}}}";

        private static async Task<(HttpStatusCode Status, string Body)> PostAsync(SensorApiTestHost host, string route, string body, string key)
        {
            using var deadline = new CancellationTokenSource(Patience);
            using var http = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(host.HttpBase, route))
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };

            request.Headers.Add("ClientName", ClientName);

            if (key is not null)
                request.Headers.Add("Key", key);

            using var response = await http.SendAsync(request, deadline.Token);

            return (response.StatusCode, await response.Content.ReadAsStringAsync(deadline.Token));
        }

        // A collector session, past its hello frame.
        private async Task<ClientWebSocket> OpenSessionAsync(SensorApiTestHost host)
        {
            using var deadline = new CancellationTokenSource(Patience);

            var socket = new ClientWebSocket();

            socket.Options.SetRequestHeader("ClientName", ClientName);
            socket.Options.SetRequestHeader("Key", _key);
            socket.Options.AddSubProtocol("hsm.sensors.v1");

            await socket.ConnectAsync(host.SessionUri, deadline.Token);

            Assert.Equal("hello", (string)(await ReceiveAsync(socket))["op"]);

            return socket;
        }

        // One request frame and its ack.
        private static async Task<JsonNode> SendAsync(ClientWebSocket socket, int id, string op, string body)
        {
            using var deadline = new CancellationTokenSource(Patience);

            await socket.SendAsync(Encoding.UTF8.GetBytes($"{{\"id\":{id},\"op\":\"{op}\",\"body\":{body}}}"), WebSocketMessageType.Text, true, deadline.Token);

            var ack = await ReceiveAsync(socket);

            Assert.Equal(id, (int)ack["ack"]);

            return ack;
        }

        private static async Task<JsonNode> ReceiveAsync(ClientWebSocket socket)
        {
            using var deadline = new CancellationTokenSource(Patience);

            var message = new ArrayBufferWriter<byte>();

            while (true)
            {
                var result = await socket.ReceiveAsync(message.GetMemory(4096), deadline.Token);

                Assert.Equal(WebSocketMessageType.Text, result.MessageType);

                message.Advance(result.Count);

                if (result.EndOfMessage)
                    return JsonNode.Parse(Encoding.UTF8.GetString(message.WrittenSpan));
            }
        }
    }
}
