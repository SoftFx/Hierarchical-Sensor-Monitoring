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
    // POST /api/sensors/version and the Type 8 element of a /api/sensors/list batch
    // (#1671): a Version value must be accepted like every other sensor type, and a
    // malformed one must be a 400 about the field, never a 500. The bodies are written
    // out because the tests pin the wire contract, including the QA form without a
    // Status field.
    public class VersionSensorHttpTests : MonitoringCoreTestsBase<CollectorSessionFixture>
    {
        private const string ClientName = "version-tests";
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

        private readonly string _key = TestProductsManager.TestProductKey.Id;


        public VersionSensorHttpTests(CollectorSessionFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture)
        {
        }


        [Fact]
        public async Task VersionRoute_AcceptsAVersionStringAndEchoesItAsAnObject()
        {
            await using var host = await StartHost();

            var (status, body) = await PostAsync(host, "version",
                "{\"Path\":\"version/string\",\"Value\":\"1.2.3.4\",\"Time\":\"2026-11-11T10:00:00Z\",\"Status\":1}");

            Assert.Equal(HttpStatusCode.OK, status);

            // The echo is a JSON object with the version as a string, not the whole
            // object folded into a JSON string.
            var echoed = JsonNode.Parse(body);
            Assert.Equal("1.2.3.4", (string)echoed["Value"]);
            Assert.Equal("version/string", (string)echoed["Path"]);

            await SensorStored("version/string");
        }

        [Fact]
        public async Task VersionRoute_WithoutStatus_DefaultsToOk()
        {
            await using var host = await StartHost();

            var (status, body) = await PostAsync(host, "version",
                "{\"Path\":\"version/no-status\",\"Value\":\"1.2.3\"}");

            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal(1, (int)JsonNode.Parse(body)["Status"]);

            await SensorStored("version/no-status");
        }

        [Fact]
        public async Task VersionRoute_AcceptsTheComponentObjectForm()
        {
            await using var host = await StartHost();

            var (status, body) = await PostAsync(host, "version",
                "{\"Path\":\"version/object\",\"Value\":{\"Major\":1,\"Minor\":2,\"Build\":3,\"Revision\":4},\"Status\":1}");

            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal("1.2.3.4", (string)JsonNode.Parse(body)["Value"]);

            await SensorStored("version/object");
        }

        [Theory]
        [InlineData("\"abc\"", "is not a valid version string")]
        [InlineData("null", "must be a version string")]
        [InlineData("\"1.2.3.4.5\"", "is not a valid version string")]
        public async Task VersionRoute_RejectsAMalformedValueWith400(string rawValue, string expectedError)
        {
            await using var host = await StartHost();

            var (status, body) = await PostAsync(host, "version",
                $"{{\"Path\":\"version/bad\",\"Value\":{rawValue},\"Status\":1}}");

            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.Contains(expectedError, body);
        }

        [Fact]
        public async Task VersionRoute_RejectsAMalformedTimeWith400()
        {
            await using var host = await StartHost();

            var (status, body) = await PostAsync(host, "version",
                "{\"Path\":\"version/bad-time\",\"Value\":\"1.2.3\",\"Time\":\"not-a-time\",\"Status\":1}");

            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.Contains("is not a valid Time", body);
        }

        [Fact]
        public async Task ListBatch_AcceptsAVersionElementWithoutStatus()
        {
            await using var host = await StartHost();

            var (status, _) = await PostAsync(host, "list",
                "[{\"Type\":8,\"Path\":\"version/in-list\",\"Value\":\"1.0.0\"}]");

            Assert.Equal(HttpStatusCode.OK, status);

            await SensorStored("version/in-list");
        }


        private async Task SensorStored(string path) =>
            await TestWait.UntilAsync(() => _valuesCache.TryGetSensorByPath(TestProductsManager.ProductId, path, out _), $"the version value of '{path}' to reach its sensor");

        private Task<SensorApiTestHost> StartHost() => SensorApiTestHost.StartAsync(_valuesCache, new CollectorSessionsConfig());

        private async Task<(HttpStatusCode Status, string Body)> PostAsync(SensorApiTestHost host, string route, string body)
        {
            using var http = new HttpClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(host.HttpBase, route))
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };

            request.Headers.Add("ClientName", ClientName);
            request.Headers.Add("Key", _key);

            using var response = await http.SendAsync(request, new CancellationTokenSource(Patience).Token);

            return (response.StatusCode, await response.Content.ReadAsStringAsync());
        }
    }
}
