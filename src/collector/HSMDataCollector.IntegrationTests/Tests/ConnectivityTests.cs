using System;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.IntegrationTests.Fixtures;
using HSMDataCollector.Options;
using Xunit;

namespace HSMDataCollector.IntegrationTests.Tests
{
    [Trait("Category", "Integration")]
    [Collection("HSM Server")]
    public class ConnectivityTests
    {
        private readonly HsmServerFixture _fixture;

        public ConnectivityTests(HsmServerFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task TestConnection_WithValidServer_ReturnsOk()
        {
            using var collector = new DataCollector(_fixture.CreateCollectorOptions());

            var result = await collector.TestConnection();

            Assert.True(result.IsOk, $"Expected OK, got {result.Code}: {result.Error}");
        }


        [Fact]
        public async Task TestConnection_WithWrongPort_ReturnsError()
        {
            var options = new CollectorOptions
            {
                ServerAddress = _fixture.ServerAddress,
                Port = _fixture.MappedSensorPort + 1,
                AccessKey = _fixture.AccessKey,
            };
            using var collector = new DataCollector(options);

            var result = await collector.TestConnection();

            Assert.False(result.IsOk);
            Assert.NotNull(result.Error);
        }

        [Fact]
        public async Task TestConnection_WithInvalidAccessKey_ReturnsError()
        {
            var options = _fixture.CreateCollectorOptions();
            options.AccessKey = Guid.NewGuid().ToString();
            using var collector = new DataCollector(options);

            var result = await collector.TestConnection();

            Assert.False(result.IsOk);
        }

        [Fact(Skip = "Docker Desktop WSL2 does not preserve port mappings after container restart")]
        public async Task TestConnection_AfterServerRestart_ReturnsOk()
        {
            using var collector = new DataCollector(_fixture.CreateCollectorOptions());

            var beforeRestart = await collector.TestConnection();
            Assert.True(beforeRestart.IsOk);

            await _fixture.StopContainerAsync();
            await Task.Delay(TimeSpan.FromSeconds(2));
            await _fixture.StartContainerAsync();

            var afterRestart = await collector.TestConnection();
            Assert.True(afterRestart.IsOk, $"Expected OK after restart, got {afterRestart.Code}: {afterRestart.Error}");
        }
    }
}
