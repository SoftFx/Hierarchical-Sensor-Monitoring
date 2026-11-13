using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
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

        // Two commands that fault on every repeat, whatever the server's state. The status
        // they get is not the point here (they are the request's fault, and may one day be
        // answered as such); the point is that neither keeps the registration next to it
        // from being applied.
        [Theory]
        [InlineData("null")]                            // no command at all
        [InlineData("{\"Type\":0,\"SensorType\":1}")]   // a registration without a path
        public async Task Commands_ACommandThatFaultsOnEveryRepeat_DoesNotKeepTheNextOneFromBeingApplied(string faulty)
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig());

            var path = $"commands/poison/{(faulty == "null" ? "after-null" : "after-pathless")}";

            await PostAsync(host, "commands", $"[{faulty},{Registration(path)}]", _key);

            Assert.True(Registered(path));
        }

        // A registration the server cannot convert is refused, not faulted: the same request
        // fails the same way on every repeat, so a 500 would make a collector send the whole
        // package again on every cycle, for good. It is an error for its own path in a 200,
        // and the rest of the package is applied.
        [Theory]
        [InlineData("\"SensorType\":99", UnknownValue)]                        // a sensor type this server does not know
        [InlineData("\"SensorType\":1,\"OriginalUnit\":123456", UnknownValue)] // a unit this server does not know
        [InlineData("\"SensorType\":1,\"TTLs\":[600000000,1200000000],\"TtlAlerts\":[{}]", MoreTtlsThanAlerts)]
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
        public async Task AddOrUpdate_ARegistrationTheServerCannotConvert_Is406_WithTheReason(string unconvertible, string reason)
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig());

            var answer = await PostAsync(host, "addOrUpdate", Registration("single/convert/refused", unconvertible), _key);

            Assert.Equal(HttpStatusCode.NotAcceptable, answer.Status);
            Assert.StartsWith($"{CannotConvert}: {reason}", answer.Body);
            Assert.False(Registered("single/convert/refused"));
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
