using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.IntegrationTests.Fixtures;
using HSMDataCollector.IntegrationTests.Helpers;
using Xunit;

namespace HSMDataCollector.IntegrationTests.Tests
{
    [Trait("Category", "Integration")]
    [Collection("HSM Server")]
    public class BatchSendingTests
    {
        private readonly HsmServerFixture _fixture;

        public BatchSendingTests(HsmServerFixture fixture)
        {
            _fixture = fixture;
        }


        [Fact]
        public async Task SendMultipleValuesInList_ServerReceivesAll()
        {
            var basePath = CollectorOptionsHelper.UniqueSensorPath("batch");
            var options = _fixture.CreateCollectorOptions();
            using var collector = new DataCollector(options);
            await collector.Start();

            var boolSensor = collector.CreateBoolSensor($"{basePath}/bool");
            var intSensor = collector.CreateIntSensor($"{basePath}/int");
            var doubleSensor = collector.CreateDoubleSensor($"{basePath}/double");
            var stringSensor = collector.CreateStringSensor($"{basePath}/string");

            boolSensor.AddValue(true);
            intSensor.AddValue(42);
            doubleSensor.AddValue(3.14);
            stringSensor.AddValue("hello");

            using var verifier = new ServerVerificationHelper(_fixture.ServerAddress, _fixture.MappedSensorPort, _fixture.AccessKey);

            var boolValues = await verifier.WaitForAndGetAllValuesAsync(CollectorOptionsHelper.ServerPath(options, $"{basePath}/bool"), 1, CollectorOptionsHelper.VerificationTimeout);
            Assert.Single(boolValues);
            Assert.Equal(true.ToString(), boolValues[0]);

            var intValues = await verifier.WaitForAndGetAllValuesAsync(CollectorOptionsHelper.ServerPath(options, $"{basePath}/int"), 1, CollectorOptionsHelper.VerificationTimeout);
            Assert.Single(intValues);
            Assert.Equal(42.ToString(), intValues[0]);

            var doubleValues = await verifier.WaitForAndGetAllValuesAsync(CollectorOptionsHelper.ServerPath(options, $"{basePath}/double"), 1, CollectorOptionsHelper.VerificationTimeout);
            Assert.Single(doubleValues);
            Assert.Equal(3.14.ToString(), doubleValues[0]);

            var stringValues = await verifier.WaitForAndGetAllValuesAsync(CollectorOptionsHelper.ServerPath(options, $"{basePath}/string"), 1, CollectorOptionsHelper.VerificationTimeout);
            Assert.Single(stringValues);
            Assert.Equal("hello", stringValues[0]);

            await collector.Stop();
        }

        [Fact]
        public async Task SendLargeBatch_ExceedingMaxValuesInPackage_SentAsMultiplePackages()
        {
            var path = CollectorOptionsHelper.UniqueSensorPath("large_batch");
            var options = _fixture.CreateCollectorOptions();
            options.MaxValuesInPackage = 5;

            using var collector = new DataCollector(options);
            await collector.Start();

            var sensor = collector.CreateIntSensor(path);
            for (int i = 0; i < 12; i++)
                sensor.AddValue(i);

            var serverPath = CollectorOptionsHelper.ServerPath(options, path);
            using var verifier = new ServerVerificationHelper(_fixture.ServerAddress, _fixture.MappedSensorPort, _fixture.AccessKey);
            var values = await verifier.WaitForAndGetAllValuesAsync(serverPath, 12, CollectorOptionsHelper.VerificationTimeout);
            Assert.Equal(12, values.Count);
            var expected = Enumerable.Range(0, 12).Select(i => i.ToString()).ToList();
            Assert.Equal(expected, values);

            await collector.Stop();
        }
    }
}
