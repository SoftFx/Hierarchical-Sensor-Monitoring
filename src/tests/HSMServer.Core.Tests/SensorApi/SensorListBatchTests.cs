using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.SensorApi
{
    // POST /api/sensors/list (#1672): a malformed ELEMENT — an unknown, missing or
    // non-numeric Type discriminator — must refuse the whole batch with a 400 that
    // names the problem, never a 500, and must store nothing, not even the valid
    // elements of a mixed batch. The bodies are written out because the tests pin
    // the wire contract.
    public class SensorListBatchTests : MonitoringCoreTestsBase<CollectorSessionFixture>
    {
        private const string ClientName = "list-tests";
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

        private readonly string _key = TestProductsManager.TestProductKey.Id;


        public SensorListBatchTests(CollectorSessionFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture)
        {
        }


        [Theory]
        [InlineData("[{\"Type\":99,\"Path\":\"list/bad\",\"Value\":1}]", "Unexpected sensor type")]
        [InlineData("[{\"Type\":2147483647,\"Path\":\"list/bad\",\"Value\":1}]", "Unexpected sensor type")]
        [InlineData("[{\"Type\":1.5,\"Path\":\"list/bad\",\"Value\":1}]", "Malformed sensor values")]
        [InlineData("[{\"Type\":2147483648,\"Path\":\"list/bad\",\"Value\":1}]", "Malformed sensor values")]
        [InlineData("[{\"Path\":\"list/no-type\",\"Value\":1}]", "Unexpected string for sensor values deserialization")]
        [InlineData("[{\"Type\":\"1\",\"Path\":\"list/string-type\",\"Value\":7}]", "Type discriminator must be a number")]
        [InlineData("{}", "Malformed sensor values")]
        public async Task AMalformedElement_RefusesTheWholeBatchWith400(string body, string expectedError)
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new ServerConfiguration.CollectorSessionsConfig());

            var (status, response) = await PostAsync(host, "list", body);

            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.Contains(expectedError, response);
        }

        [Fact]
        public async Task AMixedBatch_RefusesWith400_AndStoresNothing()
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new ServerConfiguration.CollectorSessionsConfig());

            var (status, _) = await PostAsync(host, "list",
                "[{\"Type\":1,\"Path\":\"list/int\",\"Value\":1},{\"Type\":2,\"Path\":\"list/double\",\"Value\":1.5},{\"Type\":99,\"Path\":\"list/bad\",\"Value\":1}]");

            Assert.Equal(HttpStatusCode.BadRequest, status);

            Assert.False(_valuesCache.TryGetSensorByPath(TestProductsManager.ProductId, "list/int", out _), "the valid int element must not be stored");
            Assert.False(_valuesCache.TryGetSensorByPath(TestProductsManager.ProductId, "list/double", out _), "the valid double element must not be stored");
            Assert.False(_valuesCache.TryGetSensorByPath(TestProductsManager.ProductId, "list/bad", out _), "the malformed element must not be stored");
        }

        [Fact]
        public async Task AMalformedBody_ReportsThePositionOnceAndOneBased()
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new ServerConfiguration.CollectorSessionsConfig());

            // System.Text.Json's own message already carries its 0-based
            // '| LineNumber: 0 | BytePositionInLine: N' suffix — the binder must not
            // append a second position to it.
            var (_, parserError) = await PostAsync(host, "list", "{}");
            Assert.Contains("LineNumber: 0", parserError);
            Assert.DoesNotContain("(line", parserError);

            // A converter-thrown message has no position of its own; the binder
            // appends one with a 1-based line, so a one-line body reports line 1.
            var (_, converterError) = await PostAsync(host, "list", "[{\"Type\":99,\"Path\":\"list/bad\",\"Value\":1}]");
            Assert.Contains("(line 1, byte", converterError);
        }

        [Fact]
        public async Task AValidBatch_IsStillAccepted()
        {
            await using var host = await SensorApiTestHost.StartAsync(_valuesCache, new ServerConfiguration.CollectorSessionsConfig());

            var (status, _) = await PostAsync(host, "list", "[{\"Type\":1,\"Path\":\"list/ok\",\"Value\":1}]");

            Assert.Equal(HttpStatusCode.OK, status);

            await TestWait.UntilAsync(() => _valuesCache.TryGetSensorByPath(TestProductsManager.ProductId, "list/ok", out _), "the value to reach its sensor");
        }


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
