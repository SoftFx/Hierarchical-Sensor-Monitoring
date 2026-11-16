using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.IntegrationTests.Fixtures;
using HSMDataCollector.IntegrationTests.Helpers;
using Xunit;

namespace HSMDataCollector.IntegrationTests.Tests
{
    /// <summary>
    /// The rest of this suite runs on the default transport, a collector session (ADR-0019).
    /// HTTP stays a transport a collector can choose, so it keeps a check against the real
    /// server here, next to one that names the session explicitly.
    /// </summary>
    [Trait("Category", "Integration")]
    [Collection("HSM Server")]
    public class TransportChoiceTests
    {
        private readonly HsmServerFixture _fixture;

        public TransportChoiceTests(HsmServerFixture fixture)
        {
            _fixture = fixture;
        }

        [Theory]
        [InlineData(CollectorTransport.Http)]
        [InlineData(CollectorTransport.Session)]
        public async Task TestConnection_OnEitherTransport_ReturnsOk(CollectorTransport transport)
        {
            var options = _fixture.CreateCollectorOptions();
            options.Transport = transport;
            using var collector = new DataCollector(options);

            var result = await collector.TestConnection();

            Assert.True(result.IsOk, $"Expected OK over {transport}, got {result.Code}: {result.Error}");
        }

        [Theory]
        [InlineData(CollectorTransport.Http)]
        [InlineData(CollectorTransport.Session)]
        public async Task SendIntValue_OnEitherTransport_ServerReceivesIt(CollectorTransport transport)
        {
            var path = CollectorOptionsHelper.UniqueSensorPath($"int_over_{transport}");
            var options = _fixture.CreateCollectorOptions();
            options.Transport = transport;
            using var collector = new DataCollector(options);
            await collector.Start();

            var sensor = collector.CreateIntSensor(path);
            sensor.AddValue(7);

            var serverPath = CollectorOptionsHelper.ServerPath(options, path);
            using var verifier = new ServerVerificationHelper(_fixture.ServerAddress, _fixture.MappedSensorPort, _fixture.AccessKey);
            var values = await verifier.WaitForAndGetAllValuesAsync(serverPath, 1, CollectorOptionsHelper.VerificationTimeout);
            Assert.Single(values);
            Assert.Equal(7.ToString(), values[0]);

            await collector.Stop();
        }
    }
}
