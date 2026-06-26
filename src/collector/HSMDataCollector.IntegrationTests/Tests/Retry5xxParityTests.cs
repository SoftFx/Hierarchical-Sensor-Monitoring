using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Client;
using HSMDataCollector.Core;
using HSMDataCollector.IntegrationTests.Helpers;
using HSMDataCollector.Logging;
using HSMSensorDataObjects.SensorRequests;
using HSMSensorDataObjects.SensorValueRequests;
using Xunit;

namespace HSMDataCollector.IntegrationTests.Tests
{
    /// <summary>
    /// Reproducing scenario + regression lock for the #1096 4xx/5xx ShouldHandle fix
    /// (BaseHandlers.ShouldRetry). Before the fix the Polly pipeline only retried on EXCEPTIONS,
    /// so a returned 5xx was treated as a final outcome — the data path recovered only via the
    /// slower queue re-enqueue (one attempt per collect cycle, each logged as an error). The fix
    /// makes the BOUNDED data/priority/file pipelines retry 5xx within their own budget, while the
    /// UNBOUNDED command pipeline deliberately stays exceptions-only so a persistent 5xx can never
    /// hang it forever. These tests drive the real HsmHttpsClient handler stack against the
    /// in-process fake server, so they exercise the actual Polly pipelines, not a mock.
    /// </summary>
    [Trait("Category", "Integration")]
    [Trait("Category", "FakeServerE2E")]
    public sealed class Retry5xxParityTests
    {
        private static CollectorOptions OptionsFor(FakeHsmServer server) => new CollectorOptions
        {
            ServerAddress = server.ServerAddress,
            Port = server.Port,
            AccessKey = "retry-parity-key",
            ClientName = "retry-parity-client",
            AllowPlaintextTransport = true,
        };

        [Fact]
        public async Task DataPipeline_RetriesTransient5xx_WithinOneSend_AndSucceeds()
        {
            using var server = new FakeHsmServer();
            server.FailNextDataRequests(2); // 503, 503, then 200

            using var client = new HsmHttpsClient(OptionsFor(server), new LoggerManager());

            var value = new IntSensorValue { Path = "retry/data/int", Value = 7 };
            var info = await client.SendDataAsync(new[] { value }, CancellationToken.None);

            // The fix: the bounded data pipeline retried the two 503s internally and landed the 200,
            // so the single SendDataAsync reports success. Pre-fix this returned the first 503
            // (IsSuccess == false), forcing recovery onto the queue re-enqueue path instead.
            Assert.True(info.IsSuccess, $"Expected success after transient 5xx retries; Error = {info.Error}");

            // Three HTTP attempts inside ONE SendDataAsync proves Polly retried (not queue re-enqueue,
            // which would be one attempt per call). Pre-fix this count would be 1.
            Assert.Equal(3, server.DataRequests.Count(r => r.Body.Contains("retry/data/int")));
        }

        [Fact]
        public async Task CommandPipeline_5xx_IsNotResultRetried_ReturnsWithoutHanging()
        {
            using var server = new FakeHsmServer();
            server.FailNextCommandRequests(1); // one 503 on /commands

            using var client = new HsmHttpsClient(OptionsFor(server), new LoggerManager());

            var command = new AddOrUpdateSensorRequest { Path = "retry/command/int" };
            var info = await client.SendCommandAsync(new[] { command }, CancellationToken.None);

            // The command pipeline is unbounded (int.MaxValue retries) but exceptions-only: a 5xx is
            // a returned result, so ShouldRetry says no and the send returns the failure immediately.
            // This is the guard that keeps a persistent server error from hanging commands forever.
            Assert.False(info.IsSuccess);
            Assert.Equal(1, server.CommandRequests.Count(r => r.Body.Contains("retry/command/int")));
        }
    }
}
