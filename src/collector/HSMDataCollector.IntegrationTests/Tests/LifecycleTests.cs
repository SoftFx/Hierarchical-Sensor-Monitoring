using System.Collections.Generic;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.IntegrationTests.Fixtures;
using HSMDataCollector.IntegrationTests.Helpers;
using Xunit;

namespace HSMDataCollector.IntegrationTests.Tests
{
    [Trait("Category", "Integration")]
    [Collection("HSM Server")]
    public class LifecycleTests
    {
        private readonly HsmServerFixture _fixture;

        public LifecycleTests(HsmServerFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task Start_SetsStatusToRunning()
        {
            using var collector = new DataCollector(_fixture.CreateCollectorOptions());

            Assert.Equal(CollectorStatus.Stopped, collector.Status);

            await collector.Start();

            Assert.Equal(CollectorStatus.Running, collector.Status);

            await collector.Stop();
        }

        [Fact]
        public async Task Stop_SetsStatusToStopped()
        {
            using var collector = new DataCollector(_fixture.CreateCollectorOptions());

            await collector.Start();
            Assert.Equal(CollectorStatus.Running, collector.Status);

            await collector.Stop();

            Assert.Equal(CollectorStatus.Stopped, collector.Status);
        }


        [Fact]
        public async Task LifecycleEvents_FireInCorrectOrder()
        {
            using var collector = new DataCollector(_fixture.CreateCollectorOptions());
            var events = new List<string>();

            collector.ToStarting += () => events.Add("Starting");
            collector.ToRunning += () => events.Add("Running");
            collector.ToStopping += () => events.Add("Stopping");
            collector.ToStopped += () => events.Add("Stopped");

            await collector.Start();
            await collector.Stop();

            Assert.Equal(new[] { "Starting", "Running", "Stopping", "Stopped" }, events);
        }

        [Fact]
        public async Task Restart_SendsDataSuccessfullyAfterRestart()
        {
            var path = CollectorOptionsHelper.UniqueSensorPath("restart_sensor");
            var options = _fixture.CreateCollectorOptions();
            using var collector = new DataCollector(options);

            await collector.Start();
            await collector.Stop();

            Assert.Equal(CollectorStatus.Stopped, collector.Status);

            await collector.Start();
            Assert.Equal(CollectorStatus.Running, collector.Status);

            var sensor = collector.CreateIntSensor(path);
            sensor.AddValue(99);

            var serverPath = CollectorOptionsHelper.ServerPath(options, path);
            using var verifier = new ServerVerificationHelper(_fixture.ServerAddress, _fixture.MappedSensorPort, _fixture.AccessKey);
            var values = await verifier.WaitForAndGetAllValuesAsync(serverPath, 1, CollectorOptionsHelper.VerificationTimeout);
            Assert.Single(values);
            Assert.Equal(99.ToString(), values[0]);

            await collector.Stop();
        }

        [Fact]
        public async Task Dispose_StopsCollector()
        {
            var collector = new DataCollector(_fixture.CreateCollectorOptions());
            await collector.Start();
            Assert.Equal(CollectorStatus.Running, collector.Status);

            collector.Dispose();

            // After Dispose, status is the terminal Disposed state (new in 3.40.32). Sensors and
            // queues are stopped and disposed; the collector cannot be restarted.
            Assert.Equal(CollectorStatus.Disposed, collector.Status);
        }
    }
}
