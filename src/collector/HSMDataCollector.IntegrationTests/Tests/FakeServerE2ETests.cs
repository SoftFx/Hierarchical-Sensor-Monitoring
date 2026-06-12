using System;
using System.Linq;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.IntegrationTests.Helpers;
using HSMDataCollector.Options;
using Xunit;

namespace HSMDataCollector.IntegrationTests.Tests
{
    /// <summary>
    /// Fake-server E2E smoke lane (#1094): the collector runs as itself against an in-process
    /// HTTP server over a real socket — no Docker, no TLS. Asserts on what actually reaches the
    /// wire (auth headers, the serialized value) and the retry/re-enqueue path under injected
    /// failures. These are deliberately few; the in-proc conformance corpus owns the broad
    /// behavior matrix, this layer proves the real HttpClient stack is wired correctly.
    /// The native collector joins this lane once it grows an HTTP transport (#1096).
    /// </summary>
    [Trait("Category", "Integration")]
    [Trait("Category", "FakeServerE2E")]
    public sealed class FakeServerE2ETests
    {
        private const string AccessKey = "fake-server-access-key";
        private const string ClientName = "fake-server-client";

        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

        private static CollectorOptions OptionsFor(FakeHsmServer server) => new CollectorOptions
        {
            ServerAddress = server.ServerAddress,
            Port = server.Port,
            AccessKey = AccessKey,
            ClientName = ClientName,
            AllowPlaintextTransport = true,
            PackageCollectPeriod = TimeSpan.FromMilliseconds(500),
        };

        [Fact]
        public async Task SentValue_ReachesServer_WithAuthHeadersAndPayload()
        {
            using var server = new FakeHsmServer();
            using var collector = new DataCollector(OptionsFor(server));

            await collector.Start();
            var sensor = collector.CreateIntSensor("e2e/headers/int");
            sensor.AddValue(31337);

            var dataRequest = await WaitForAsync(
                () => server.DataRequests.FirstOrDefault(r => r.Body.Contains("31337")),
                r => r != null);

            await collector.Stop();

            Assert.Equal(AccessKey, dataRequest.Key);
            Assert.Equal(ClientName, dataRequest.ClientName);
            Assert.StartsWith("/api/sensors/", dataRequest.Path);
            Assert.Contains("31337", dataRequest.Body);
        }

        [Fact]
        public async Task SensorRegistration_ReachesServer_AsCommandWithPath()
        {
            using var server = new FakeHsmServer();
            using var collector = new DataCollector(OptionsFor(server));

            await collector.Start();
            collector.CreateIntSensor("e2e/registration/int");

            // A registration is an AddOrUpdateSensorRequest; the command queue batches commands
            // and posts them to /commands (the single-item /addOrUpdate route is only used when a
            // lone request is sent, not the batched queue path).
            var registration = await WaitForAsync(
                () => server.Requests.FirstOrDefault(r =>
                    r.Path.EndsWith("/commands", StringComparison.Ordinal) && r.Body.Contains("e2e/registration/int")),
                r => r != null);

            await collector.Stop();

            Assert.True(registration != null,
                "No /commands request carried the registration. Paths seen: " + string.Join(", ", server.Requests.Select(r => r.Method + " " + r.Path)));
            Assert.Equal(AccessKey, registration.Key);
            Assert.Equal(ClientName, registration.ClientName);
        }

        [Fact]
        public async Task TransientServerFailure_ValueEventuallyLands_ViaReEnqueue()
        {
            using var server = new FakeHsmServer();
            // The default Polly pipeline retries only exceptions, not 5xx — but the queue
            // re-enqueues a package whose send returned a non-success status, so the value
            // survives a transient 503 and lands on a later collect cycle.
            server.FailNextDataRequests(2);

            using var collector = new DataCollector(OptionsFor(server));

            await collector.Start();
            var sensor = collector.CreateIntSensor("e2e/retry/int");
            sensor.AddValue(4242);

            // Eventually a data request with the value is answered 200 (the run records both the
            // failed and the successful attempts; we only need the value to have reached the wire
            // after the injected failures were consumed).
            var landed = await WaitForAsync(
                () => server.DataRequests.Count(r => r.Body.Contains("4242")),
                count => count >= 3); // 2 failed attempts + at least one more

            await collector.Stop();

            Assert.True(landed >= 3, $"Expected the value to be retried past 2 injected failures, saw {landed} attempts.");
        }

        [Fact]
        public async Task TestConnection_AgainstReachableServer_ReturnsOk()
        {
            using var server = new FakeHsmServer();
            using var collector = new DataCollector(OptionsFor(server));

            var result = await collector.TestConnection();

            Assert.True(result.IsOk, $"Expected OK, got {result.Code}: {result.Error}");
            Assert.Contains(server.Requests, r => r.Path.EndsWith("/testConnection", StringComparison.Ordinal));
        }

        // Polls a producer until the predicate holds or the timeout elapses, then returns the last
        // value (so the caller's assertions produce a meaningful message on timeout).
        private static async Task<T> WaitForAsync<T>(Func<T> produce, Func<T, bool> done)
        {
            var deadline = DateTime.UtcNow + Timeout;
            var value = produce();

            while (!done(value) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50).ConfigureAwait(false);
                value = produce();
            }

            return value;
        }
    }
}
