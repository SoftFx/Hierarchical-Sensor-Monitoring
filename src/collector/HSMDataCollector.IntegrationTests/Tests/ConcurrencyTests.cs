using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.IntegrationTests.Fixtures;
using HSMDataCollector.IntegrationTests.Helpers;
using HSMDataCollector.PublicInterface;
using Xunit;

namespace HSMDataCollector.IntegrationTests.Tests
{
    [Trait("Category", "Integration")]
    [Collection("HSM Server")]
    public class ConcurrencyTests : IClassFixture<HsmServerFixture>
    {
        private readonly HsmServerFixture _fixture;

        public ConcurrencyTests(HsmServerFixture fixture)
        {
            _fixture = fixture;
        }


        [Fact]
        public async Task MultipleSensorsSendingConcurrently_AllDataReceived()
        {
            var basePath = CollectorOptionsHelper.UniqueSensorPath("concurrent");
            var options = _fixture.CreateCollectorOptions();
            using var collector = new DataCollector(options);
            await collector.Start();

            var sensors = new List<IInstantValueSensor<int>>();
            var paths = new List<string>();

            for (int i = 0; i < 10; i++)
            {
                var path = $"{basePath}/sensor_{i}";
                paths.Add(path);
                sensors.Add(collector.CreateIntSensor(path));
            }

            var tasks = sensors.Select((s, i) => Task.Run(() => s.AddValue(i * 10)));
            await Task.WhenAll(tasks);

            using var verifier = new ServerVerificationHelper(_fixture.ServerAddress, _fixture.MappedSensorPort, _fixture.AccessKey);

            for (int i = 0; i < 10; i++)
            {
                var serverPath = CollectorOptionsHelper.ServerPath(options, paths[i]);
                var values = await verifier.WaitForAndGetAllValuesAsync(serverPath, 1, CollectorOptionsHelper.VerificationTimeout);
                Assert.Single(values);
                Assert.Equal((i * 10).ToString(), values[0]);
            }

            await collector.Stop();
        }

        [Fact]
        public async Task HighVolumeSending_NoDataLoss()
        {
            var path = CollectorOptionsHelper.UniqueSensorPath("highvolume");
            var options = _fixture.CreateCollectorOptions();
            using var collector = new DataCollector(options);
            await collector.Start();

            var sensor = collector.CreateIntSensor(path);
            for (int i = 0; i < 100; i++)
                sensor.AddValue(i);

            var serverPath = CollectorOptionsHelper.ServerPath(options, path);
            using var verifier = new ServerVerificationHelper(_fixture.ServerAddress, _fixture.MappedSensorPort, _fixture.AccessKey);

            // First wait for at least some data to arrive
            var found = await verifier.WaitForValueAsync(serverPath, 1, CollectorOptionsHelper.VerificationTimeout);
            Assert.True(found, $"No data received at path: {serverPath}");

            // Then verify all values
            var values = await verifier.WaitForAndGetAllValuesAsync(serverPath, 100, TimeSpan.FromSeconds(120));
            Assert.Equal(100, values.Count);
            var expected = Enumerable.Range(0, 100).Select(i => i.ToString()).ToList();
            Assert.Equal(expected, values);

            await collector.Stop();
        }
    }
}
