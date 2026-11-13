using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Middleware;
using HSMServer.SensorApi;
using HSMServer.ServerConfiguration;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.SensorApi
{
    public sealed class RegistrationStatusFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(RegistrationStatusTests);
    }


    // What the two registration routes answer, POST /api/sensors/commands and
    // POST /api/sensors/addOrUpdate, and why the status matters (#1749): a collector takes a
    // 4xx on a registration as a final refusal and never sends that package again, and
    // repeats a 5xx on its next cycle. So a fault of the server must be a 500, and a
    // registration the server refuses must not be one.
    //
    // The real Sensor API (middleware, controller, ingestion service) on a real Kestrel
    // listener over the real cache; FaultingCache makes one registration fail.
    //
    // The statuses and texts are written out here on purpose: the tests pin the contract,
    // not the server's constants.
    public class RegistrationStatusTests : MonitoringCoreTestsBase<RegistrationStatusFixture>
    {
        private const string ClientName = "registration-status-tests";
        private const string CannotConvert = "The registration cannot be converted";
        private const string UnknownValue = "it carries a value this server does not know";
        private const string MoreTtlsThanAlerts = "TTLs count (2) exceeds TtlAlerts count (1)";
        private const string NullAlert = "one of its alerts is null";
        private const string NullAlertCondition = "one of its alert conditions is null";

        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

        private readonly string _key = TestProductsManager.TestProductKey.Id;


        public RegistrationStatusTests(RegistrationStatusFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture) { }


        // A 500 means "some commands faulted, the others were applied; send the package
        // again". The commands after the faulted one must be applied too: a collector sends
        // a package answered 5xx again every cycle, ahead of what it issues later, so if the
        // server stopped at the fault, a command that faults on every repeat would keep
        // every later registration of that collector from ever being applied.
        [Fact]
        public async Task Commands_FaultOnOneCommand_Is500_TheOthersAreApplied_AndTheResendAppliesTheFaultedOne()
        {
            var faulting = FaultingCache.Wrap(_valuesCache);

            await using var host = await SensorApiTestHost.StartAsync(faulting.Cache, new CollectorSessionsConfig());

            var package = $"[{Registration("commands/fault/before")},{Registration("commands/fault/faulted")},{Registration("commands/fault/after")}]";

            faulting.FaultOn("commands/fault/faulted");

            var answer = await PostAsync(host, "commands", package, _key);

            Assert.Equal(HttpStatusCode.InternalServerError, answer.Status);

            // The body the 400 used to carry: the per-path errors of the refused commands.
            // Here there were none, and a faulted command has no entry of its own.
            Assert.Empty(JsonNode.Parse(answer.Body).AsObject());

            // Everything but the faulted command is applied, the one after it included.
            Assert.True(Registered("commands/fault/before"));
            Assert.False(Registered("commands/fault/faulted"));
            Assert.True(Registered("commands/fault/after"));

            // What a collector does with a 500: the same package again on its next cycle.
            // The fault has passed, and nothing of the package is lost.
            faulting.Recover();

            var resend = await PostAsync(host, "commands", package, _key);

            Assert.Equal(HttpStatusCode.OK, resend.Status);
            Assert.Empty(JsonNode.Parse(resend.Body).AsObject());
            Assert.True(Registered("commands/fault/before"));
            Assert.True(Registered("commands/fault/faulted"));
            Assert.True(Registered("commands/fault/after"));
        }

        [Fact]
        public async Task Commands_FaultAmongRefusedCommands_Is500_WithThePerPathErrorsOfTheRefusedOnes()
        {
            var faulting = FaultingCache.Wrap(_valuesCache);

            await using var host = await SensorApiTestHost.StartAsync(faulting.Cache, new CollectorSessionsConfig());

            faulting.FaultOn("commands/partial/faulted");

            var answer = await PostAsync(host, "commands",
                $"[{Registration("commands/partial/refused-before", "\"SensorType\":99")},{Registration("commands/partial/faulted")},{Registration("commands/partial/refused-after", "\"SensorType\":99")}]", _key);

            Assert.Equal(HttpStatusCode.InternalServerError, answer.Status);

            var errors = JsonNode.Parse(answer.Body).AsObject();

            Assert.Equal(2, errors.Count);
            Assert.StartsWith(CannotConvert, (string)errors["commands/partial/refused-before"]);
            Assert.StartsWith(CannotConvert, (string)errors["commands/partial/refused-after"]);
        }

        // A list with a null element or with a command without a path is the request's
        // fault, the same on every repeat: the whole request is a 400 that says what is
        // wrong, and nothing of it is applied. Not a 500, which a collector would send
        // again every cycle; and not an error per path, as there is no path to put it under.
        [Theory]
        [InlineData("null", "The command at index 1 is null.")]
        [InlineData("{\"Type\":0,\"SensorType\":1}", "The command at index 1 has no path.")]
        public async Task Commands_ANullCommandOrOneWithoutAPath_Is400_AndNothingIsApplied(string malformed, string reason)
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig());

            var folder = $"commands/malformed/{(malformed == "null" ? "null" : "pathless")}";

            var answer = await PostAsync(host, "commands", $"[{Registration($"{folder}/before")},{malformed},{Registration($"{folder}/after")}]", _key);

            Assert.Equal(HttpStatusCode.BadRequest, answer.Status);
            Assert.Contains(reason, answer.Body);
            Assert.False(Registered($"{folder}/before"));
            Assert.False(Registered($"{folder}/after"));
        }

        // A registration the server cannot convert is refused, not faulted: the same request
        // fails the same way on every repeat, so a 500 would make a collector send the whole
        // package again on every cycle, for good. It is an error for its own path in a 200,
        // and the rest of the package is applied.
        [Theory]
        [InlineData("\"SensorType\":99", UnknownValue)]                        // a sensor type this server does not know
        [InlineData("\"SensorType\":1,\"OriginalUnit\":123456", UnknownValue)] // a unit this server does not know
        [InlineData("\"SensorType\":1,\"TTLs\":[600000000,1200000000],\"TtlAlerts\":[{}]", MoreTtlsThanAlerts)]
        [InlineData("\"SensorType\":1,\"Alerts\":[{\"Conditions\":[{\"Operation\":200}]}]", UnknownValue)] // an alert operation this server does not know
        [InlineData("\"SensorType\":1,\"Alerts\":[null]", NullAlert)]
        [InlineData("\"SensorType\":1,\"TtlAlerts\":[null]", NullAlert)]
        [InlineData("\"SensorType\":1,\"Alerts\":[{\"Conditions\":[null]}]", NullAlertCondition)]
        public async Task Commands_ARegistrationTheServerCannotConvert_Is200_WithItsPerPathError(string unconvertible, string reason)
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig());

            var answer = await PostAsync(host, "commands",
                $"[{Registration("commands/convert/refused", unconvertible)},{Registration("commands/convert/next-to-refused")}]", _key);

            Assert.Equal(HttpStatusCode.OK, answer.Status);

            var errors = JsonNode.Parse(answer.Body).AsObject();

            Assert.Single(errors);
            Assert.StartsWith($"{CannotConvert}: {reason}", (string)errors["commands/convert/refused"]);

            Assert.False(Registered("commands/convert/refused"));
            Assert.True(Registered("commands/convert/next-to-refused"));
        }

        // A refused key is an error per path in a 200 when the key rides the body (no Key
        // header: with one, the middleware answers 406 before the route). It stays that.
        [Fact]
        public async Task Commands_AKeyTheServerRefuses_Is200_WithItsPerPathError()
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig());

            var unknownKey = Guid.NewGuid().ToString();

            var answer = await PostAsync(host, "commands", $"[{Registration("commands/key/refused", $"\"SensorType\":1,\"Key\":\"{unknownKey}\"")}]", key: null);

            Assert.Equal(HttpStatusCode.OK, answer.Status);

            var errors = JsonNode.Parse(answer.Body).AsObject();

            Assert.Single(errors);
            Assert.Equal("Key is invalid.", (string)errors["commands/key/refused"]);
            Assert.False(Registered("commands/key/refused"));
        }

        // A body the server cannot read is the request's fault: a 400, and nothing is applied.
        // The same body is unreadable on every repeat, so it must not be the status that
        // tells a collector to send it again.
        [Theory]
        [InlineData("[{\"Type\":0,\"Path\":\"commands/unreadable\",\"SensorType\":")]                // cut off
        [InlineData("[{\"Type\":0,\"Path\":\"commands/unreadable\",\"SensorType\":\"not-a-type\"}]")] // a member of the wrong kind
        [InlineData("[{\"Type\":7,\"Path\":\"commands/unreadable\"}]")]                              // a command the server does not know
        [InlineData("{\"Type\":0,\"Path\":\"commands/unreadable\",\"SensorType\":1}")]               // not a list
        [InlineData("null")]
        [InlineData("")]
        public async Task Commands_ABodyTheServerCannotRead_Is400(string body)
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig());

            var answer = await PostAsync(host, "commands", body, _key);

            Assert.Equal(HttpStatusCode.BadRequest, answer.Status);
            Assert.Contains("The body cannot be read as a list of sensor commands.", answer.Body);
            Assert.False(Registered("commands/unreadable"));
        }

        [Fact]
        public async Task AddOrUpdate_FaultWhileApplying_Is500_WithTheBodyThe400Had()
        {
            var faulting = FaultingCache.Wrap(_valuesCache);

            await using var host = await SensorApiTestHost.StartAsync(faulting.Cache, new CollectorSessionsConfig());

            faulting.FaultOn("single/fault/faulted");

            var answer = await PostAsync(host, "addOrUpdate", Registration("single/fault/faulted"), _key);

            Assert.Equal(HttpStatusCode.InternalServerError, answer.Status);
            Assert.StartsWith("Failed to update sensor! Update request: {", answer.Body);
            Assert.Contains("single/fault/faulted", answer.Body);
            Assert.False(Registered("single/fault/faulted"));

            // The repeat, once the fault has passed.
            faulting.Recover();

            Assert.Equal(HttpStatusCode.OK, (await PostAsync(host, "addOrUpdate", Registration("single/fault/faulted"), _key)).Status);
            Assert.True(Registered("single/fault/faulted"));
        }

        // The single-registration route has no dictionary: its refusal is a 406 with the
        // reason as the body.
        [Theory]
        [InlineData("\"SensorType\":99", UnknownValue)]
        [InlineData("\"SensorType\":1,\"OriginalUnit\":123456", UnknownValue)]
        [InlineData("\"SensorType\":1,\"TTLs\":[600000000,1200000000],\"TtlAlerts\":[{}]", MoreTtlsThanAlerts)]
        [InlineData("\"SensorType\":1,\"Alerts\":[{\"Conditions\":[{\"Operation\":200}]}]", UnknownValue)]
        [InlineData("\"SensorType\":1,\"Alerts\":[null]", NullAlert)]
        [InlineData("\"SensorType\":1,\"TtlAlerts\":[null]", NullAlert)]
        [InlineData("\"SensorType\":1,\"Alerts\":[{\"Conditions\":[null]}]", NullAlertCondition)]
        public async Task AddOrUpdate_ARegistrationTheServerCannotConvert_Is406_WithTheReason(string unconvertible, string reason)
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig());

            var answer = await PostAsync(host, "addOrUpdate", Registration("single/convert/refused", unconvertible), _key);

            Assert.Equal(HttpStatusCode.NotAcceptable, answer.Status);
            Assert.StartsWith($"{CannotConvert}: {reason}", answer.Body);
            Assert.False(Registered("single/convert/refused"));
        }

        // The single route has no list to refuse as a whole: a registration without a path
        // is refused like any other the server cannot convert.
        [Fact]
        public async Task AddOrUpdate_ARegistrationWithoutAPath_Is406_WithTheReason()
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig());

            var answer = await PostAsync(host, "addOrUpdate", "{\"Type\":0,\"SensorType\":1}", _key);

            Assert.Equal(HttpStatusCode.NotAcceptable, answer.Status);
            Assert.Equal($"{CannotConvert}: it has no path", answer.Body);
        }

        // The other side of the split: only what the request causes is a refusal. What the
        // server knows about the caller is read outside the conversion's catch, so when it
        // fails (here the product went away under the request) the service does not answer
        // "cannot be converted", which a collector would take as final: the exception
        // leaves it, and both transports answer an exception with a 500 (the tests above).
        [Fact]
        public async Task AFaultOfTheCallersState_LeavesTheServiceAsAnException_NotAsARefusal()
        {
            Assert.True(_valuesCache.TryGetKey(Guid.Parse(_key), out var key, out _));

            var service = new SensorIngestionService(_valuesCache, new RecordingTelemetry());
            var caller = new PublicApiRequestInfo { Product = null, Key = key, CollectorName = ClientName, RemoteIP = "127.0.0.1" };
            var registration = new AddOrUpdateSensorRequest { Path = "service/fault/registration", SensorType = SensorType.IntSensor };

            await Assert.ThrowsAsync<NullReferenceException>(() => service.AddOrUpdateSensorAsync(caller, registration));

            Assert.False(Registered("service/fault/registration"));
        }

        [Fact]
        public async Task AddOrUpdate_AKeyTheServerRefuses_Is406_WithTheReason()
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig());

            var unknownKey = Guid.NewGuid().ToString();

            var answer = await PostAsync(host, "addOrUpdate", Registration("single/key/refused", $"\"SensorType\":1,\"Key\":\"{unknownKey}\""), key: null);

            Assert.Equal(HttpStatusCode.NotAcceptable, answer.Status);
            Assert.Equal("Key is invalid.", answer.Body);
            Assert.False(Registered("single/key/refused"));
        }

        // A body the server cannot read is the request's fault and stays a 400: nothing was
        // applied, and the same body is unreadable on every repeat.
        [Theory]
        [InlineData("{\"Type\":0,\"Path\":\"single/unreadable\",\"SensorType\":")]
        [InlineData("{\"Type\":0,\"Path\":\"single/unreadable\",\"SensorType\":\"not-a-type\"}")]
        [InlineData("[]")]
        public async Task AddOrUpdate_ABodyTheServerCannotRead_Is400(string body)
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig());

            var answer = await PostAsync(host, "addOrUpdate", body, _key);

            Assert.Equal(HttpStatusCode.BadRequest, answer.Status);
            Assert.False(Registered("single/unreadable"));
        }


        private bool Registered(string path) => _valuesCache.TryGetSensorByPath(TestProductsManager.ProductId, path, out _);

        private static string Registration(string path, string members = "\"SensorType\":1") => $"{{\"Type\":0,\"Path\":\"{path}\",{members}}}";

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
    }
}
