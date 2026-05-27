using System;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.IntegrationTests.Fixtures;
using HSMDataCollector.IntegrationTests.Helpers;
using Xunit;

namespace HSMDataCollector.IntegrationTests.Tests
{
    [Trait("Category", "Integration")]
    [Trait("Category", "NetworkFailure")]
    [Collection("HSM Server")]
    public class NetworkFailureTests : IClassFixture<HsmServerFixture>
    {
        private readonly HsmServerFixture _fixture;

        public NetworkFailureTests(HsmServerFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task SendData_WhenServerDown_QueuedAndSentOnRecovery()
        {
            var path = CollectorOptionsHelper.UniqueSensorPath("down_sensor");
            var options = _fixture.CreateCollectorOptions();
            using var collector = new DataCollector(options);

            await collector.Start();

            await _fixture.StopContainerAsync();

            var sensor = collector.CreateIntSensor(path);
            sensor.AddValue(42);

            await _fixture.StartContainerAsync();

            var serverPath = CollectorOptionsHelper.ServerPath(options, path);
            using var verifier = new ServerVerificationHelper(_fixture.ServerAddress, _fixture.MappedSensorPort, _fixture.AccessKey);

            var found = await verifier.WaitForValueAsync(serverPath, 1, TimeSpan.FromSeconds(60));
            Assert.True(found, $"Queued value not received after server recovery at path: {serverPath}");

            var values = await verifier.WaitForAndGetAllValuesAsync(serverPath, 1, CollectorOptionsHelper.VerificationTimeout);
            Assert.Single(values);
            Assert.Equal(42.ToString(), values[0]);

            await collector.Stop();
        }


        [Fact]
        public async Task TestConnection_WhenServerDown_ReturnsConnectionError()
        {
            using var collector = new DataCollector(_fixture.CreateCollectorOptions());

            await _fixture.StopContainerAsync();

            var result = await collector.TestConnection();
            Assert.False(result.IsOk);
            Assert.NotNull(result.Error);

            await _fixture.StartContainerAsync();
        }


        [Fact]
        public async Task TransientFailure_DataResentSuccessfully()
        {
            var path = CollectorOptionsHelper.UniqueSensorPath("transient_sensor");
            var options = _fixture.CreateCollectorOptions();
            using var collector = new DataCollector(options);

            await collector.Start();

            var sensor = collector.CreateIntSensor(path);
            sensor.AddValue(100);

            var serverPath = CollectorOptionsHelper.ServerPath(options, path);
            using var verifier = new ServerVerificationHelper(_fixture.ServerAddress, _fixture.MappedSensorPort, _fixture.AccessKey);

            // Brief server outage
            await _fixture.StopContainerAsync();
            await Task.Delay(TimeSpan.FromSeconds(1));
            await _fixture.StartContainerAsync();

            // Data should eventually arrive via retry
            var found = await verifier.WaitForValueAsync(serverPath, 1, TimeSpan.FromSeconds(60));
            Assert.True(found, $"Value not received after transient failure at path: {serverPath}");

            var values = await verifier.WaitForAndGetAllValuesAsync(serverPath, 1, CollectorOptionsHelper.VerificationTimeout);
            Assert.Single(values);
            Assert.Equal(100.ToString(), values[0]);

            await collector.Stop();
        }
    }

    [Trait("Category", "Integration")]
    [Trait("Category", "NetworkFailure")]
    [Collection("HSM Server with Proxy")]
    public class ToxiproxyNetworkTests : IClassFixture<HsmServerWithProxyFixture>
    {
        private readonly HsmServerWithProxyFixture _fixture;

        public ToxiproxyNetworkTests(HsmServerWithProxyFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task SlowNetwork_DataStillDelivered()
        {
            var path = CollectorOptionsHelper.UniqueSensorPath("slow_sensor");
            var options = _fixture.CreateCollectorOptions();
            using var collector = new DataCollector(options);

            await _fixture.AddLatencyAsync(2000);

            await collector.Start();

            var sensor = collector.CreateIntSensor(path);
            sensor.AddValue(42);

            var serverPath = CollectorOptionsHelper.ServerPath(options, path);
            using var verifier = new ServerVerificationHelper(_fixture.ServerAddress, _fixture.ProxyPort, _fixture.AccessKey);

            var found = await verifier.WaitForValueAsync(serverPath, 1, TimeSpan.FromSeconds(30));
            Assert.True(found, $"Value not received with slow network at path: {serverPath}");

            var values = await verifier.WaitForAndGetAllValuesAsync(serverPath, 1, CollectorOptionsHelper.VerificationTimeout);
            Assert.Single(values);
            Assert.Equal(42.ToString(), values[0]);

            await _fixture.RemoveToxicAsync("latency");
            await collector.Stop();
        }

        [Fact]
        public async Task ConnectionReset_RetrySucceeds()
        {
            var path = CollectorOptionsHelper.UniqueSensorPath("reset_sensor");
            var options = _fixture.CreateCollectorOptions();
            using var collector = new DataCollector(options);

            await collector.Start();

            await _fixture.AddConnectionResetAsync();

            var sensor = collector.CreateIntSensor(path);
            sensor.AddValue(42);

            var serverPath = CollectorOptionsHelper.ServerPath(options, path);
            using var verifier = new ServerVerificationHelper(_fixture.ServerAddress, _fixture.ProxyPort, _fixture.AccessKey);

            var found = await verifier.WaitForValueAsync(serverPath, 1, TimeSpan.FromSeconds(30));
            Assert.True(found, $"Value not received after connection reset at path: {serverPath}");

            var values = await verifier.WaitForAndGetAllValuesAsync(serverPath, 1, CollectorOptionsHelper.VerificationTimeout);
            Assert.Single(values);
            Assert.Equal(42.ToString(), values[0]);

            await collector.Stop();
        }

        [Fact]
        public async Task BandwidthThrottling_DataEventuallyDelivered()
        {
            var path = CollectorOptionsHelper.UniqueSensorPath("throttle_sensor");
            var options = _fixture.CreateCollectorOptions();
            using var collector = new DataCollector(options);

            await _fixture.SlowDownAsync(10);

            await collector.Start();

            var sensor = collector.CreateBoolSensor(path);
            sensor.AddValue(true);

            var serverPath = CollectorOptionsHelper.ServerPath(options, path);
            using var verifier = new ServerVerificationHelper(_fixture.ServerAddress, _fixture.ProxyPort, _fixture.AccessKey);

            var found = await verifier.WaitForValueAsync(serverPath, 1, TimeSpan.FromSeconds(60));
            Assert.True(found, $"Value not received with bandwidth throttling at path: {serverPath}");

            var values = await verifier.WaitForAndGetAllValuesAsync(serverPath, 1, CollectorOptionsHelper.VerificationTimeout);
            Assert.Single(values);
            Assert.Equal(true.ToString(), values[0]);

            await _fixture.RemoveToxicAsync("bandwidth");
            await collector.Stop();
        }
    }
}
