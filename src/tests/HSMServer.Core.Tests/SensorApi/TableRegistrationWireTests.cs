using HSMServer.Core.Model;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.ServerConfiguration;
using HSMTableBlocks;
using System;
using System.Buffers;
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
    public sealed class TableRegistrationWireFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(TableRegistrationWireTests);
    }


    // The registration of a table sensor over the wire (#1845, ADR-0021 point 2): SensorType 11 with
    // TableColumns, on POST /api/sensors/commands, POST /api/sensors/addOrUpdate and the session's
    // commands op. The real Sensor API on a real Kestrel listener over the real cache.
    //
    // A refusal is asserted as a refusal: an error under its own path in a 200 (or the 406 of the
    // single route, or the per-path error of a 200 ack), with the registration next to it applied.
    // A fault would be a 500, after which a collector sends the package again; these registrations
    // fail the same way on every repeat, so a fault would be wrong.
    //
    // The wire strings are written out on purpose: the tests pin the contract, not the server's constants.
    public class TableRegistrationWireTests : MonitoringCoreTestsBase<TableRegistrationWireFixture>
    {
        private const string ClientName = "table-registration-tests";
        private const string Refused = "The table sensor registration is refused";
        private const string Quotes = "\"TableColumns\":[{\"Name\":\"time\",\"Type\":1},{\"Name\":\"bid\",\"Type\":3},{\"Name\":\"ask\",\"Type\":3}]";

        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

        private readonly string _key = TestProductsManager.TestProductKey.Id;


        public TableRegistrationWireTests(TableRegistrationWireFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture) { }


        [Fact]
        public async Task Commands_ATableRegistration_CreatesATableSensorWithItsSchema_AndTheSameAgainIsAccepted()
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig());

            var package = $"[{Table("wire/quotes/EURUSD", Quotes)}]";

            var first = await PostAsync(host, "commands", package);

            Assert.Equal(HttpStatusCode.OK, first.Status);
            Assert.Empty(JsonNode.Parse(first.Body).AsObject());

            var table = Assert.IsType<TableSensorModel>(Sensor("wire/quotes/EURUSD"));
            var expected = new TableSchema([new("time", TableColumnType.Time), new("bid", TableColumnType.Double), new("ask", TableColumnType.Double)]);

            Assert.True(table.Schema.SameAs(expected));
            Assert.Equal(expected.Id, table.Schema.Id);

            // A collector registers all its sensors again at every start.
            var again = await PostAsync(host, "commands", package);

            Assert.Equal(HttpStatusCode.OK, again.Status);
            Assert.Empty(JsonNode.Parse(again.Body).AsObject());
            Assert.Same(table, Sensor("wire/quotes/EURUSD"));
        }

        [Fact]
        public async Task AddOrUpdate_ATableRegistration_Is200_AndARefusedOneIs406WithTheReason()
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig());

            var applied = await PostAsync(host, "addOrUpdate", Table("wire/single/applied", Quotes));

            Assert.Equal(HttpStatusCode.OK, applied.Status);
            Assert.IsType<TableSensorModel>(Sensor("wire/single/applied"));

            var refused = await PostAsync(host, "addOrUpdate", Table("wire/single/refused", "\"TableColumns\":[{\"Name\":\"bid\",\"Type\":3}]"));

            Assert.Equal(HttpStatusCode.NotAcceptable, refused.Status);
            Assert.StartsWith(Refused, refused.Body);
            Assert.Equal($"{Refused}: its table schema is invalid: the first column of a table schema must be the time", refused.Body);
            Assert.Null(Sensor("wire/single/refused"));
        }

        // Every one of these fails the same way on every repeat, so it is a refusal under its own
        // path in a 200, and the registration after it in the same package is applied.
        [Theory]
        [InlineData("\"SensorType\":11,\"TableColumns\":[{\"Name\":\"bid\",\"Type\":3}]", "the first column of a table schema must be the time")]
        [InlineData("\"SensorType\":11,\"TableColumns\":[{\"Name\":\"time\",\"Type\":1},{\"Name\":\"bid\",\"Type\":3},{\"Name\":\"bid\",\"Type\":3}]", "column name 'bid' is used twice")]
        [InlineData("\"SensorType\":11,\"TableColumns\":[]", "a table schema needs at least one column")]
        [InlineData("\"SensorType\":11,\"TableColumns\":[{\"Name\":\"time\",\"Type\":1},{\"Name\":\"x\",\"Type\":7}]", "column 1 has the unknown type 7")]
        [InlineData("\"SensorType\":11,\"TableColumns\":[{\"Name\":\"time\",\"Type\":1},{\"Name\":\"x\",\"Type\":257}]", "column 1 has the unknown type 257")]
        [InlineData("\"SensorType\":11,\"TableColumns\":[{\"Name\":\"time\",\"Type\":1},null]", "column 1 is null")]
        [InlineData("\"SensorType\":11,\"TableColumns\":[{\"Name\":\"time\",\"Type\":1},{\"Name\":\"\",\"Type\":3}]", "column 1 has no name")]
        [InlineData("\"SensorType\":1,\"TableColumns\":[{\"Name\":\"time\",\"Type\":1}]", "only a registration of a table sensor (SensorType 11) carries table columns")]
        [InlineData("\"TableColumns\":[{\"Name\":\"time\",\"Type\":1}]", "only a registration of a table sensor (SensorType 11) carries table columns")]
        public async Task Commands_ATableRegistrationTheServerRefuses_Is200_WithItsPerPathError(string members, string reason)
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig());

            var path = $"wire/refused/{Guid.NewGuid():N}";
            var next = $"wire/next/{Guid.NewGuid():N}";

            var answer = await PostAsync(host, "commands", $"[{Registration(path, members)},{Table(next, Quotes)}]");

            Assert.Equal(HttpStatusCode.OK, answer.Status);

            var errors = JsonNode.Parse(answer.Body).AsObject();

            Assert.Single(errors);
            Assert.StartsWith(Refused, (string)errors[path]);
            Assert.Contains(reason, (string)errors[path]);
            Assert.Null(Sensor(path));
            Assert.IsType<TableSensorModel>(Sensor(next));
        }

        // More columns than the configured limit (64 by default).
        [Fact]
        public async Task Commands_ATableSchemaOverTheColumnLimit_IsRefused()
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig());

            var columns = string.Join(",", new[] { "{\"Name\":\"time\",\"Type\":1}" }.Concat(Enumerable.Range(1, 64).Select(i => $"{{\"Name\":\"c{i}\",\"Type\":2}}")));

            var answer = await PostAsync(host, "commands", $"[{Table("wire/wide", $"\"TableColumns\":[{columns}]")}]");

            Assert.Equal(HttpStatusCode.OK, answer.Status);

            var error = (string)JsonNode.Parse(answer.Body)["wire/wide"];

            Assert.StartsWith(Refused, error);
            Assert.Contains("at most 64 columns, this one has 65", error);
            Assert.Null(Sensor("wire/wide"));
        }

        // A registration with a very large TableColumns array is refused by its count, as any schema
        // over the limit, before its columns are mapped.
        [Fact]
        public async Task Commands_ATableSchemaWithAVeryLargeColumnArray_IsRefused()
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig());

            var columns = string.Join(",", new[] { "{\"Name\":\"time\",\"Type\":1}" }.Concat(Enumerable.Range(1, 9_999).Select(i => $"{{\"Name\":\"c{i}\",\"Type\":2}}")));

            var answer = await PostAsync(host, "commands", $"[{Table("wire/huge", $"\"TableColumns\":[{columns}]")}]");

            Assert.Equal(HttpStatusCode.OK, answer.Status);

            var error = (string)JsonNode.Parse(answer.Body)["wire/huge"];

            Assert.StartsWith(Refused, error);
            Assert.Contains("at most 64 columns, this one has 10000", error);
            Assert.Null(Sensor("wire/huge"));
        }

        // The column limit is the configured TableSensors.MaxColumnsPerTable, not the default of 64:
        // a host configured with 8 refuses a schema of 9 columns and takes one of 8.
        [Fact]
        public async Task Commands_TheConfiguredColumnLimit_IsTheOneApplied()
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig(), tableLimits: new TableBlockLimits(maxColumnsPerTable: 8));

            static string Columns(int count) => string.Join(",", new[] { "{\"Name\":\"time\",\"Type\":1}" }
                .Concat(Enumerable.Range(1, count - 1).Select(i => $"{{\"Name\":\"c{i}\",\"Type\":3}}")));

            var answer = await PostAsync(host, "commands",
                $"[{Table("wire/limit/nine", $"\"TableColumns\":[{Columns(9)}]")},{Table("wire/limit/eight", $"\"TableColumns\":[{Columns(8)}]")}]");

            Assert.Equal(HttpStatusCode.OK, answer.Status);

            var errors = JsonNode.Parse(answer.Body).AsObject();
            var error = (string)errors["wire/limit/nine"];

            Assert.StartsWith(Refused, error);
            Assert.Contains("at most 8 columns, this one has 9", error);
            Assert.Null(Sensor("wire/limit/nine"));

            Assert.False(errors.ContainsKey("wire/limit/eight"));
            Assert.Equal(8, Assert.IsType<TableSensorModel>(Sensor("wire/limit/eight")).Schema.Columns.Count);
        }

        // The table identifier inside a block is the registered path byte for byte (#1844), so a
        // table sensor is registered only under the normalized form of its path, which the
        // refusal names. Nothing is created under the other spelling.
        [Theory]
        [InlineData("/wire/spelling/a")]
        [InlineData("wire//spelling/a")]
        [InlineData("wire/ spelling /a")]
        [InlineData("wire/spelling/a/")]
        public async Task Commands_ATableRegistrationWithAPathNotInNormalForm_IsRefused_NamingTheNormalForm(string path)
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig());

            var answer = await PostAsync(host, "commands", $"[{Table(path, Quotes)}]");

            Assert.Equal(HttpStatusCode.OK, answer.Status);

            var error = (string)JsonNode.Parse(answer.Body)[path];

            Assert.StartsWith(Refused, error);
            Assert.Contains("\"wire/spelling/a\"", error);
            Assert.Null(Sensor("wire/spelling/a"));
        }

        // The cache's own rules (#1844) reach the wire as refusals too.
        [Fact]
        public async Task Commands_AnotherSchemaOnATablePath_AndATableWithoutColumns_AreRefusals()
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig());

            Assert.Equal(HttpStatusCode.OK, (await PostAsync(host, "commands", $"[{Table("wire/shape", Quotes)}]")).Status);

            var answer = await PostAsync(host, "commands",
                $"[{Table("wire/shape", "\"TableColumns\":[{\"Name\":\"time\",\"Type\":1},{\"Name\":\"bid\",\"Type\":3}]")},{Registration("wire/no-columns", "\"SensorType\":11")}]");

            Assert.Equal(HttpStatusCode.OK, answer.Status);

            var errors = JsonNode.Parse(answer.Body).AsObject();

            Assert.Contains("a different shape is a different sensor", (string)errors["wire/shape"]);
            Assert.Contains("a table registration needs a schema", (string)errors["wire/no-columns"]);
            Assert.Equal(3, ((TableSensorModel)Sensor("wire/shape")).Schema.Columns.Count);
            Assert.Null(Sensor("wire/no-columns"));
        }

        // Retention of a table sensor is the sensor's history setting (owner decision of
        // 2026-11-24): a registration carries it as for any sensor, and it is stored. Applying it
        // is #1850.
        [Fact]
        public async Task Commands_ATableRegistrationWithKeepHistory_StoresTheSetting()
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig());

            var twoWeeks = TimeSpan.FromDays(14).Ticks;

            var answer = await PostAsync(host, "commands", $"[{Table("wire/retained", $"{Quotes},\"KeepHistory\":{twoWeeks}")}]");

            Assert.Equal(HttpStatusCode.OK, answer.Status);
            Assert.Empty(JsonNode.Parse(answer.Body).AsObject());

            var table = Assert.IsType<TableSensorModel>(Sensor("wire/retained"));

            Assert.Equal(twoWeeks, table.Settings.KeepHistory.Value.Ticks);
        }

        // The session's commands op reads the same body with the same service: an applied table
        // registration and a refused one, in one frame, answered as the HTTP route answers.
        [Fact]
        public async Task Session_ATableRegistration_IsAppliedAndRefusedAsOverHttp()
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig());
            using var socket = new ClientWebSocket();

            socket.Options.SetRequestHeader("ClientName", ClientName);
            socket.Options.SetRequestHeader("Key", _key);
            socket.Options.AddSubProtocol("hsm.sensors.v1");

            await socket.ConnectAsync(host.SessionUri, Deadline());
            Assert.Equal("hello", (string)(await ReceiveAsync(socket))["op"]);

            var body = $"[{Table("wire/session/applied", Quotes)},{Table("/wire/session/refused", Quotes)}]";

            await socket.SendAsync(Encoding.UTF8.GetBytes($"{{\"id\":1,\"op\":\"commands\",\"body\":{body}}}"), WebSocketMessageType.Text, true, Deadline());

            var ack = await ReceiveAsync(socket);

            Assert.Equal(1, (long)ack["ack"]);
            Assert.Equal(200, (int)ack["status"]);

            var errors = ack["body"].AsObject();

            Assert.Single(errors);
            Assert.StartsWith(Refused, (string)errors["/wire/session/refused"]);
            Assert.IsType<TableSensorModel>(Sensor("wire/session/applied"));
            Assert.Null(Sensor("wire/session/refused"));
        }


        private BaseSensorModel Sensor(string path) => _valuesCache.TryGetSensorByPath(TestProductsManager.ProductId, path, out var sensor) ? sensor : null;

        private static string Registration(string path, string members) => $"{{\"Type\":0,\"Path\":\"{path}\",{members}}}";

        private static string Table(string path, string members) => Registration(path, $"\"SensorType\":11,{members}");

        private static CancellationToken Deadline() => new CancellationTokenSource(Patience).Token;

        private static async Task<JsonNode> ReceiveAsync(ClientWebSocket socket)
        {
            var message = new ArrayBufferWriter<byte>();
            var deadline = Deadline();

            while (true)
            {
                var result = await socket.ReceiveAsync(message.GetMemory(4096), deadline);

                Assert.True(result.MessageType == WebSocketMessageType.Text, $"expected a text frame, the session answered {result.MessageType} ({socket.CloseStatus}: {socket.CloseStatusDescription})");

                message.Advance(result.Count);

                if (result.EndOfMessage)
                    return JsonNode.Parse(Encoding.UTF8.GetString(message.WrittenSpan));
            }
        }

        private async Task<(HttpStatusCode Status, string Body)> PostAsync(SensorApiTestHost host, string route, string body)
        {
            using var deadline = new CancellationTokenSource(Patience);
            using var http = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(host.HttpBase, route))
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };

            request.Headers.Add("ClientName", ClientName);
            request.Headers.Add("Key", _key);

            using var response = await http.SendAsync(request, deadline.Token);

            return (response.StatusCode, await response.Content.ReadAsStringAsync(deadline.Token));
        }
    }
}
