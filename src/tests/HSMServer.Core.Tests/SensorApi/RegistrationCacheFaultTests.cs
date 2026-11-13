using HSMCommon.Model;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.SensorApi;
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

        // The two ways a store fails on the row of a sensor. "throws": the write raises an
        // exception. "reports": the way the real store fails, where the LevelDB layer catches
        // the failed write, logs it and returns; only the checked write of the registration
        // path is told.
        public const string Throws = "throws";
        public const string Reports = "reports";

        // Names of sensors whose row the store fails to write, once each.
        private readonly ConcurrentDictionary<string, byte> _failCreationOnce = new();
        private readonly ConcurrentDictionary<string, byte> _failSaveOnce = new();
        private readonly ConcurrentDictionary<string, byte> _loseCreationOnce = new();
        private readonly ConcurrentDictionary<string, byte> _loseSaveOnce = new();

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
                ShouldLoseSensorAdd = entity => _loseCreationOnce.TryRemove(entity.DisplayName, out _),
                ShouldLoseSensorUpdate = entity => _loseSaveOnce.TryRemove(entity.DisplayName, out _),
            };

        private void FailCreationOnce(string how, string sensorName) => (how == Throws ? _failCreationOnce : _loseCreationOnce)[sensorName] = 0;

        private void FailSaveOnce(string how, string sensorName) => (how == Throws ? _failSaveOnce : _loseSaveOnce)[sensorName] = 0;


        [Theory]
        [InlineData(Throws)]
        [InlineData(Reports)]
        public async Task StoreFailsOnce_WhileASensorIsCreated_Is500OnBothTransports_AndTheResendRegistersIt(string how)
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig());
            using var socket = await OpenSessionAsync(host);

            string Package(string transport) =>
                $"[{Registration($"created/{how}/{transport}/before")},{Registration($"created/{how}/{transport}/failed-{transport}-{how}", "from the resend")},{Registration($"created/{how}/{transport}/after")}]";

            FailCreationOnce(how, $"failed-http-{how}");
            FailCreationOnce(how, $"failed-session-{how}");

            var http = await PostAsync(host, "commands", Package("http"), _key);
            var ack = await SendAsync(socket, 1, "commands", Package("session"));

            Assert.Equal(HttpStatusCode.InternalServerError, http.Status);
            Assert.Equal(500, (int)ack["status"]);
            Assert.Equal("failed to apply the commands", (string)ack["error"]);

            foreach (var transport in new[] { "http", "session" })
            {
                // The rest of the package is applied, as for any fault of one command. The
                // sensor whose row was not written is rolled back: it is not left in memory
                // without a row.
                Assert.True(Registered($"created/{how}/{transport}/before"), $"{transport}: the command before the failed one");
                Assert.False(Registered($"created/{how}/{transport}/failed-{transport}-{how}"), $"{transport}: the command the store failed on");
                Assert.True(Registered($"created/{how}/{transport}/after"), $"{transport}: the command after the failed one");
            }

            // The store works again (it failed once), and the collector sends the package again.
            var httpResend = await PostAsync(host, "commands", Package("http"), _key);
            var resent = await SendAsync(socket, 2, "commands", Package("session"));

            Assert.Equal(HttpStatusCode.OK, httpResend.Status);
            Assert.Equal(200, (int)resent["status"]);

            foreach (var transport in new[] { "http", "session" })
            {
                Assert.True(_valuesCache.TryGetSensorByPath(TestProductsManager.ProductId, $"created/{how}/{transport}/failed-{transport}-{how}", out var sensor), $"{transport}: the resend");
                Assert.Equal("from the resend", sensor.Description);
                Assert.Equal("from the resend", StoredDescription($"failed-{transport}-{how}"));
            }
        }

        [Theory]
        [InlineData(Throws)]
        [InlineData(Reports)]
        public async Task StoreFailsOnce_WhileTheSettingsAreSaved_Is500OnBothTransports_AndTheResendSavesThem(string how)
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig());
            using var socket = await OpenSessionAsync(host);

            var overHttp = $"saved/{how}/http/kept-http-{how}";
            var overSession = $"saved/{how}/session/kept-session-{how}";

            // The sensors exist, registered without a description.
            Assert.Equal(HttpStatusCode.OK, (await PostAsync(host, "commands", $"[{Registration(overHttp)},{Registration(overSession)}]", _key)).Status);

            FailSaveOnce(how, $"kept-http-{how}");
            FailSaveOnce(how, $"kept-session-{how}");

            var http = await PostAsync(host, "commands", $"[{Registration(overHttp, "a new description")}]", _key);
            var ack = await SendAsync(socket, 1, "commands", $"[{Registration(overSession, "a new description")}]");

            Assert.Equal(HttpStatusCode.InternalServerError, http.Status);
            Assert.Equal(500, (int)ack["status"]);

            // Nothing of it reached the store: after a restart the description would be gone.
            Assert.NotEqual("a new description", StoredDescription($"kept-http-{how}"));
            Assert.NotEqual("a new description", StoredDescription($"kept-session-{how}"));

            var httpResend = await PostAsync(host, "commands", $"[{Registration(overHttp, "a new description")}]", _key);
            var resent = await SendAsync(socket, 2, "commands", $"[{Registration(overSession, "a new description")}]");

            Assert.Equal(HttpStatusCode.OK, httpResend.Status);
            Assert.Equal(200, (int)resent["status"]);
            Assert.Equal("a new description", StoredDescription($"kept-http-{how}"));
            Assert.Equal("a new description", StoredDescription($"kept-session-{how}"));
        }

        // The same failure of the real store, seen by the callers that are not the
        // registration of a sensor: the store logs it and they are told nothing, as always.
        // A sensor a first value creates stays in memory without its row (a later save of the
        // sensor writes it), and a settings update from the web UI is answered Ok.
        [Fact]
        public async Task AWriteTheStoreReportsButDoesNotThrow_OnTheOtherCallersOfTheCache_IsNotSeen_AsBefore()
        {
            _loseCreationOnce["value-created-lost"] = 0;

            var value = SensorValuesFactory.BuildSensorValue(SensorType.Integer, "others/value-created-lost", DateTime.UtcNow);
            var stored = await _valuesCache.AddSensorValueAsync(Guid.Parse(_key), TestProductsManager.ProductId, value);

            Assert.True(stored.IsOk);
            Assert.False(stored.IsFault);
            Assert.True(Registered("others/value-created-lost"));
            Assert.DoesNotContain(_databaseCoreManager.DatabaseCore.GetAllSensors(), row => row.DisplayName == "value-created-lost");
            Assert.Empty(_loseCreationOnce);

            Assert.True(_valuesCache.TryGetSensorByPath(TestProductsManager.ProductId, "others/value-created-lost", out var sensor));

            _loseSaveOnce["value-created-lost"] = 0;

            var updated = await _valuesCache.UpdateSensorAsync(new SensorUpdate { Id = sensor.Id, Description = "from the UI" });

            Assert.True(updated.IsOk);
            Assert.False(updated.IsFault);
            Assert.Empty(_loseSaveOnce);
            Assert.Equal("from the UI", sensor.Description);
        }

        // A registration of a path that is an alias of a sensor moved to another root product
        // is not applied on the queue of the product it came for: it is forwarded to the queue
        // that owns the sensor. The collector is answered when it has ended there, and by the
        // same rule: a write of the row the store fails on is a fault, and the resend saves.
        // It used to be answered as applied at once, before that queue had stored anything.
        [Theory]
        [InlineData(Throws)]
        [InlineData(Reports)]
        public async Task StoreFailsOnce_ForARegistrationForwardedThroughAnAlias_Is500OnBothTransports_AndTheResendSavesIt(string how)
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig());
            using var socket = await OpenSessionAsync(host);

            var overHttp = await MoveToAnotherProductAsync(host, $"forwarded/{how}/http/old", $"moved-http-{how}");
            var overSession = await MoveToAnotherProductAsync(host, $"forwarded/{how}/session/old", $"moved-session-{how}");

            FailSaveOnce(how, $"moved-http-{how}");
            FailSaveOnce(how, $"moved-session-{how}");

            var http = await PostAsync(host, "commands", $"[{Registration($"forwarded/{how}/http/old", "through the alias")}]", _key);
            var ack = await SendAsync(socket, 1, "commands", $"[{Registration($"forwarded/{how}/session/old", "through the alias")}]");

            Assert.Equal(HttpStatusCode.InternalServerError, http.Status);
            Assert.Equal(500, (int)ack["status"]);
            Assert.NotEqual("through the alias", StoredDescription($"moved-http-{how}"));
            Assert.NotEqual("through the alias", StoredDescription($"moved-session-{how}"));

            var httpResend = await PostAsync(host, "commands", $"[{Registration($"forwarded/{how}/http/old", "through the alias")}]", _key);
            var resent = await SendAsync(socket, 2, "commands", $"[{Registration($"forwarded/{how}/session/old", "through the alias")}]");

            Assert.Equal(HttpStatusCode.OK, httpResend.Status);
            Assert.Equal(200, (int)resent["status"]);
            Assert.Equal("through the alias", StoredDescription($"moved-http-{how}"));
            Assert.Equal("through the alias", StoredDescription($"moved-session-{how}"));

            // The registrations went to the moved sensors: nothing was created at the old paths.
            Assert.Equal("through the alias", overHttp.Description);
            Assert.Equal("through the alias", overSession.Description);
            Assert.False(Registered($"forwarded/{how}/http/old"));
            Assert.False(Registered($"forwarded/{how}/session/old"));
        }

        // A forwarded registration that succeeds is answered as it always was, and is now
        // applied and stored by the time of the answer.
        [Fact]
        public async Task ARegistrationForwardedThroughAnAlias_ThatSucceeds_IsAnsweredAsBefore()
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig());

            var moved = await MoveToAnotherProductAsync(host, "forwarded/fine/old", "moved-fine");

            var batch = await PostAsync(host, "commands", $"[{Registration("forwarded/fine/old", "first")}]", _key);

            Assert.Equal(HttpStatusCode.OK, batch.Status);
            Assert.Empty(JsonNode.Parse(batch.Body).AsObject());
            Assert.Equal("first", moved.Description);
            Assert.Equal("first", StoredDescription("moved-fine"));

            var single = await PostAsync(host, "addOrUpdate", Registration("forwarded/fine/old", "second"), _key);

            Assert.Equal(HttpStatusCode.OK, single.Status);
            Assert.Equal(string.Empty, single.Body);
            Assert.Equal("second", StoredDescription("moved-fine"));
            Assert.False(Registered("forwarded/fine/old"));
        }

        // No handler of a queue waits for another queue: the one that forwards returns at
        // once, and the wait for the outcome is the caller's, outside any queue. So two root
        // products that forward registrations to each other at the same time cannot wait for
        // each other. Many at once in both directions, all answered.
        [Fact]
        public async Task TwoProductsForwardingRegistrationsToEachOther_AtTheSameTime_AllAnswer()
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig());

            var other = await _valuesCache.AddProductAsync($"mutual-{Guid.NewGuid():N}", Guid.Empty);

            // One sensor of each product moved to the other: each product now has an alias
            // that forwards to the other's queue.
            var here = await CreateAsync(host, "mutual/here-old");
            var there = await CreateInAsync(other.Id, "mutual/there-old");

            Assert.True((await _valuesCache.MoveSensorAsync(here.Id, other.Id, "mutual/here-new")).IsOk);
            Assert.True((await _valuesCache.MoveSensorAsync(there.Id, TestProductsManager.ProductId, "mutual/there-new")).IsOk);

            var registrations = Enumerable.Range(0, 100).Select(i => i % 2 == 0
                ? _valuesCache.AddOrUpdateSensorAsync(RegistrationRequest(TestProductsManager.ProductId, "mutual/here-old", $"to the other product {i}"))
                : _valuesCache.AddOrUpdateSensorAsync(RegistrationRequest(other.Id, "mutual/there-old", $"to this product {i}"))).ToArray();

            var results = await Task.WhenAll(registrations).WaitAsync(Patience);

            Assert.All(results, result => Assert.True(result.IsOk, result.Error));
            Assert.StartsWith("to the other product", here.Description);
            Assert.StartsWith("to this product", there.Description);
        }

        // The wait for a forwarded registration has a limit. The outcome is completed by the
        // other product's queue, and a queue that does not get to the request (here its
        // thread is held; in the field it is disposed with the request unread when its
        // product is removed or the server stops) would otherwise leave the collector's
        // request without an answer. It is a fault: 500, and the collector sends it again.
        [Fact]
        public async Task ARegistrationForwardedThroughAnAlias_ThatIsNotAnsweredInTime_Is500_NotARequestThatHangs()
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig());

            var moved = await MoveToAnotherProductAsync(host, "forwarded/late/old", "moved-late");
            var other = moved.Root.Id;
            var blocker = await CreateInAsync(other, "late/blocker");

            // The other product's queue thread is held inside a handler until the test lets go.
            var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var release = new ManualResetEventSlim();

            void Hold(BaseSensorModel sensor, ActionType action)
            {
                if (sensor.Id == blocker.Id && action == ActionType.Update)
                {
                    held.TrySetResult();
                    release.Wait(TimeSpan.FromSeconds(60));
                }
            }

            _valuesCache.ChangeSensorEvent += Hold;

            Task<HSMCommon.TaskResult.TaskResult> holding = null;
            var usualWait = _valuesCache.ForwardedRegistrationWait;

            try
            {
                holding = _valuesCache.AddOrUpdateSensorAsync(RegistrationRequest(other, "late/blocker", "holds the queue"));

                await held.Task.WaitAsync(Patience);

                _valuesCache.ForwardedRegistrationWait = TimeSpan.FromMilliseconds(100);

                // PostAsync gives up after Patience, so a wait without a limit fails the test
                // instead of hanging it.
                var answer = await PostAsync(host, "commands", $"[{Registration("forwarded/late/old", "late")}]", _key);

                Assert.Equal(HttpStatusCode.InternalServerError, answer.Status);

                var result = await _valuesCache.AddOrUpdateSensorAsync(RegistrationRequest(TestProductsManager.ProductId, "forwarded/late/old", "late")).WaitAsync(Patience);

                Assert.True(result.IsFault);
                Assert.Equal("The registration forwarded through an alias to another product was not answered within 0.1 s", result.Error);
            }
            finally
            {
                release.Set();
                _valuesCache.ChangeSensorEvent -= Hold;
                _valuesCache.ForwardedRegistrationWait = usualWait;
            }

            Assert.Equal(TimeSpan.FromSeconds(20), usualWait);

            await holding.WaitAsync(Patience);

            // The queue works again: the resend is answered, and applied.
            var resend = await PostAsync(host, "commands", $"[{Registration("forwarded/late/old", "from the resend")}]", _key);

            Assert.Equal(HttpStatusCode.OK, resend.Status);
            Assert.Equal("from the resend", moved.Description);
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

        // The mark of a failed key lookup belongs to that one lookup. In a batch with keys in
        // the bodies (no Key header) the lookup of the first command's key fails; the second
        // command is merely refused (only the first key of a request is looked up). It is a
        // refusal under its path in the body of the 500, not one more fault.
        [Fact]
        public async Task KeyLookupFails_ForOneCommand_ACommandRefusedAfterIt_IsStillARefusalUnderItsPath()
        {
            var faulting = FaultingCache.Wrap(_valuesCache);

            await using var host = await SensorApiTestHost.StartAsync(faulting.Cache, new CollectorSessionsConfig());

            faulting.FaultOnKeyLookup();

            var answer = await PostAsync(host, "commands",
                $"[{{\"Type\":0,\"Path\":\"lookup-mark/faulted\",\"SensorType\":1,\"Key\":\"{_key}\"}}," +
                $"{{\"Type\":0,\"Path\":\"lookup-mark/refused\",\"SensorType\":1,\"Key\":\"{Guid.NewGuid()}\"}}]", key: null);

            Assert.Equal(HttpStatusCode.InternalServerError, answer.Status);

            var errors = JsonNode.Parse(answer.Body).AsObject();

            Assert.Single(errors);
            Assert.Equal("Cannot process a access key in the body", (string)errors["lookup-mark/refused"]);
        }

        // The text of a fault goes into the server's log line and into the key the log
        // limiter remembers the fault by, and for a fault of the queue it carries the
        // request as text, the client's description included. So it is one line of a bounded
        // length, whatever the client sent.
        [Fact]
        public void TheCauseOfARegistrationFault_IsOneBoundedLine()
        {
            var cause = "An error was occurred while processing request: SensorAddOrUpdateRequest { Description = first line\r\nsecond line " +
                        new string('x', 5000) + " }";

            var fault = new RegistrationFaultException("fault/path", cause);

            Assert.StartsWith("The registration of \"fault/path\" was not applied: An error was occurred while processing request: SensorAddOrUpdateRequest { Description = first line??second line xxx", fault.Message);
            Assert.DoesNotContain('\r', fault.Message);
            Assert.DoesNotContain('\n', fault.Message);

            // The fixed words, the path, 300 characters of the cause and the mark of the cut.
            Assert.Equal("The registration of \"fault/path\" was not applied: ".Length + 300 + "...".Length, fault.Message.Length);
        }


        private bool Registered(string path) => _valuesCache.TryGetSensorByPath(TestProductsManager.ProductId, path, out _);

        // Registers a sensor of the test product over HTTP.
        private async Task<BaseSensorModel> CreateAsync(SensorApiTestHost host, string path)
        {
            Assert.Equal(HttpStatusCode.OK, (await PostAsync(host, "commands", $"[{Registration(path)}]", _key)).Status);
            Assert.True(_valuesCache.TryGetSensorByPath(TestProductsManager.ProductId, path, out var sensor));

            return sensor;
        }

        // Registers a sensor of another root product, straight in the cache.
        private async Task<BaseSensorModel> CreateInAsync(Guid productId, string path)
        {
            Assert.True((await _valuesCache.AddOrUpdateSensorAsync(RegistrationRequest(productId, path, description: null))).IsOk);
            Assert.True(_valuesCache.TryGetSensorByPath(productId, path, out var sensor));

            return sensor;
        }

        private static SensorAddOrUpdateRequest RegistrationRequest(Guid productId, string path, string description) =>
            new(productId, path)
            {
                Update = new SensorUpdate { Id = Guid.Empty, Description = description },
                Type = SensorType.Integer,
                TypeIsExplicit = true,
            };

        // A sensor of the test product, registered at `oldPath` and then moved to a new root
        // product under the name `movedName`: `oldPath` is from then on an alias that forwards
        // to the other product's queue.
        private async Task<BaseSensorModel> MoveToAnotherProductAsync(SensorApiTestHost host, string oldPath, string movedName)
        {
            var sensor = await CreateAsync(host, oldPath);
            var other = await _valuesCache.AddProductAsync($"alias-target-{Guid.NewGuid():N}", Guid.Empty);

            Assert.True((await _valuesCache.MoveSensorAsync(sensor.Id, other.Id, $"moved/{movedName}")).IsOk);
            Assert.False(Registered(oldPath));

            return sensor;
        }

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
