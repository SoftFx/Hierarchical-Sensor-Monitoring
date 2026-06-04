using System;
using System.Linq;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.IntegrationTests.Fixtures;
using HSMDataCollector.IntegrationTests.Helpers;
using HSMDataCollector.Options;
using Xunit;

namespace HSMDataCollector.IntegrationTests.Tests
{
    [Trait("Category", "Integration")]
    [Collection("HSM Server")]
    public class QueueBehaviorTests
    {
        private readonly HsmServerFixture _fixture;

        public QueueBehaviorTests(HsmServerFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task ValuesDroppedBeforeStart_NotDeliveredAfterStart()
        {
            var path = CollectorOptionsHelper.UniqueSensorPath("queued_sensor");
            var options = _fixture.CreateCollectorOptions();
            using var collector = new DataCollector(options);

            var sensor = collector.CreateIntSensor(path);
            sensor.AddValue(42);

            var serverPath = CollectorOptionsHelper.ServerPath(options, path);
            using var verifier = new ServerVerificationHelper(_fixture.ServerAddress, _fixture.MappedSensorPort, _fixture.AccessKey);

            // Value should not be on server yet (collector not started)
            var notFound = await verifier.WaitForAndGetAllValuesAsync(serverPath, 1, TimeSpan.FromSeconds(3));
            Assert.Empty(notFound);

            await collector.Start();

            // Values added before Start() are silently dropped (no buffering),
            // so the value should still not be on the server after Start()
            var stillNotFound = await verifier.WaitForAndGetAllValuesAsync(serverPath, 1, TimeSpan.FromSeconds(3));
            Assert.Empty(stillNotFound);

            await collector.Stop();
        }

        [Fact]
        public async Task PackageCollectPeriod_WaitingPeriodIsRespected()
        {
            var path = CollectorOptionsHelper.UniqueSensorPath("period_sensor");
            var options = _fixture.CreateCollectorOptions();
            options.PackageCollectPeriod = TimeSpan.FromSeconds(5);

            using var collector = new DataCollector(options);
            await collector.Start();

            var sensor = collector.CreateIntSensor(path);
            sensor.AddValue(10);

            var serverPath = CollectorOptionsHelper.ServerPath(options, path);
            using var verifier = new ServerVerificationHelper(_fixture.ServerAddress, _fixture.MappedSensorPort, _fixture.AccessKey);

            // Value should not arrive immediately (before collect period)
            var notFound = await verifier.WaitForAndGetAllValuesAsync(serverPath, 1, TimeSpan.FromSeconds(2));
            Assert.Empty(notFound);

            // But should arrive after collect period + processing time
            var values = await verifier.WaitForAndGetAllValuesAsync(serverPath, 1, TimeSpan.FromSeconds(10));
            Assert.Single(values);
            Assert.Equal(10.ToString(), values[0]);

            await collector.Stop();
        }

        [Fact]
        public async Task MaxQueueSize_OldestValuesDroppedOnOverflow()
        {
            var path = CollectorOptionsHelper.UniqueSensorPath("overflow_sensor");
            var options = _fixture.CreateCollectorOptions();
            options.MaxQueueSize = 5;
            options.PackageCollectPeriod = TimeSpan.FromSeconds(2);

            using var collector = new DataCollector(options);
            await collector.Start();

            var sensor = collector.CreateIntSensor(path);
            // The for loop completes in microseconds, well before the 2s PackageCollectPeriod
            // fires, so the channel overflows and trims to the newest 5 values (5-9).
            for (int i = 0; i < 10; i++)
                sensor.AddValue(i);

            var serverPath = CollectorOptionsHelper.ServerPath(options, path);
            using var verifier = new ServerVerificationHelper(_fixture.ServerAddress, _fixture.MappedSensorPort, _fixture.AccessKey);

            var values = await verifier.WaitForAndGetAllValuesAsync(serverPath, 1, TimeSpan.FromSeconds(15));
            Assert.Contains("9", values);
            Assert.DoesNotContain("0", values);

            await collector.Stop();
        }

        [Fact]
        public async Task PrioritySensor_DataSentImmediately()
        {
            var path = CollectorOptionsHelper.UniqueSensorPath("priority_sensor");
            var options = _fixture.CreateCollectorOptions();

            using var collector = new DataCollector(options);
            await collector.Start();

            var sensorOptions = new InstantSensorOptions
            {
                IsPrioritySensor = true,
            };
            var sensor = collector.CreateIntSensor(path, sensorOptions);
            sensor.AddValue(777);

            var serverPath = CollectorOptionsHelper.ServerPath(options, path);
            using var verifier = new ServerVerificationHelper(_fixture.ServerAddress, _fixture.MappedSensorPort, _fixture.AccessKey);
            // Priority sensors should arrive quickly (event-driven, not timer-based)
            var values = await verifier.WaitForAndGetAllValuesAsync(serverPath, 1, TimeSpan.FromSeconds(10));
            Assert.Single(values);
            Assert.Equal(777.ToString(), values[0]);

            await collector.Stop();
        }
    }
}
