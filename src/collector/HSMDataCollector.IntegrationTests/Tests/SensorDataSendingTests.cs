using System;
using System.IO;
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
    public class SensorDataSendingTests
    {
        private readonly HsmServerFixture _fixture;

        public SensorDataSendingTests(HsmServerFixture fixture)
        {
            _fixture = fixture;
        }


        [Fact]
        public async Task SendBoolValue_ServerReceivesCorrectData()
        {
            var path = CollectorOptionsHelper.UniqueSensorPath("bool_sensor");
            var options = _fixture.CreateCollectorOptions();
            using var collector = new DataCollector(options);
            await collector.Start();

            var sensor = collector.CreateBoolSensor(path);
            sensor.AddValue(true);

            var serverPath = CollectorOptionsHelper.ServerPath(options, path);
            using var verifier = new ServerVerificationHelper(_fixture.ServerAddress, _fixture.MappedSensorPort, _fixture.AccessKey);
            var values = await verifier.WaitForAndGetAllValuesAsync(serverPath, 1, CollectorOptionsHelper.VerificationTimeout);
            Assert.Single(values);
            Assert.Equal(true.ToString(), values[0]);

            await collector.Stop();
        }

        [Fact]
        public async Task SendIntValue_ServerReceivesCorrectData()
        {
            var path = CollectorOptionsHelper.UniqueSensorPath("int_sensor");
            var options = _fixture.CreateCollectorOptions();
            using var collector = new DataCollector(options);
            await collector.Start();

            var sensor = collector.CreateIntSensor(path);
            sensor.AddValue(42);

            var serverPath = CollectorOptionsHelper.ServerPath(options, path);
            using var verifier = new ServerVerificationHelper(_fixture.ServerAddress, _fixture.MappedSensorPort, _fixture.AccessKey);
            var values = await verifier.WaitForAndGetAllValuesAsync(serverPath, 1, CollectorOptionsHelper.VerificationTimeout);
            Assert.Single(values);
            Assert.Equal(42.ToString(), values[0]);

            await collector.Stop();
        }

        [Fact]
        public async Task SendDoubleValue_ServerReceivesCorrectData()
        {
            var path = CollectorOptionsHelper.UniqueSensorPath("double_sensor");
            var options = _fixture.CreateCollectorOptions();
            using var collector = new DataCollector(options);
            await collector.Start();

            var sensor = collector.CreateDoubleSensor(path);
            sensor.AddValue(3.14);

            var serverPath = CollectorOptionsHelper.ServerPath(options, path);
            using var verifier = new ServerVerificationHelper(_fixture.ServerAddress, _fixture.MappedSensorPort, _fixture.AccessKey);
            var values = await verifier.WaitForAndGetAllValuesAsync(serverPath, 1, CollectorOptionsHelper.VerificationTimeout);
            Assert.Single(values);
            Assert.Equal(3.14.ToString(), values[0]);

            await collector.Stop();
        }


        [Fact]
        public async Task SendStringValue_ServerReceivesCorrectData()
        {
            var path = CollectorOptionsHelper.UniqueSensorPath("string_sensor");
            var options = _fixture.CreateCollectorOptions();
            using var collector = new DataCollector(options);
            await collector.Start();

            var sensor = collector.CreateStringSensor(path);
            sensor.AddValue("hello world");

            var serverPath = CollectorOptionsHelper.ServerPath(options, path);
            using var verifier = new ServerVerificationHelper(_fixture.ServerAddress, _fixture.MappedSensorPort, _fixture.AccessKey);
            var values = await verifier.WaitForAndGetAllValuesAsync(serverPath, 1, CollectorOptionsHelper.VerificationTimeout);
            Assert.Single(values);
            Assert.Equal("hello world", values[0]);

            await collector.Stop();
        }

        [Fact(Skip = "Server bug #1068: History API returns empty for TimeSpan sensors")]
        public async Task SendTimeSpanValue_ServerReceivesCorrectData()
        {
            var path = CollectorOptionsHelper.UniqueSensorPath("timespan_sensor");
            var options = _fixture.CreateCollectorOptions();
            using var collector = new DataCollector(options);
            await collector.Start();

            var expected = TimeSpan.FromMinutes(5);
            var sensor = collector.CreateTimeSensor(path);
            sensor.AddValue(expected);

            var serverPath = CollectorOptionsHelper.ServerPath(options, path);
            using var verifier = new ServerVerificationHelper(_fixture.ServerAddress, _fixture.MappedSensorPort, _fixture.AccessKey);
            var found = await verifier.WaitForValueAsync(serverPath, 1, CollectorOptionsHelper.VerificationTimeout);
            Assert.True(found, $"TimeSpan sensor value not received at path: {serverPath}");

            await collector.Stop();
        }


        [Fact(Skip = "Server bug #1068: History API returns empty for Version sensors")]
        public async Task SendVersionValue_ServerReceivesCorrectData()
        {
            var path = CollectorOptionsHelper.UniqueSensorPath("version_sensor");
            var options = _fixture.CreateCollectorOptions();
            using var collector = new DataCollector(options);
            await collector.Start();

            var expected = new Version(1, 2, 3);
            var sensor = collector.CreateVersionSensor(path);
            sensor.AddValue(expected);

            var serverPath = CollectorOptionsHelper.ServerPath(options, path);
            using var verifier = new ServerVerificationHelper(_fixture.ServerAddress, _fixture.MappedSensorPort, _fixture.AccessKey);
            var found = await verifier.WaitForValueAsync(serverPath, 1, CollectorOptionsHelper.VerificationTimeout);
            Assert.True(found, $"Version sensor value not received at path: {serverPath}");

            await collector.Stop();
        }

        [Fact]
        public async Task SendRateValue_ServerReceivesCorrectData()
        {
            var path = CollectorOptionsHelper.UniqueSensorPath("rate_sensor");
            var options = _fixture.CreateCollectorOptions();
            using var collector = new DataCollector(options);
            await collector.Start();

            var sensor = collector.CreateRateSensor(path, null);
            sensor.AddValue(100.0);

            var serverPath = CollectorOptionsHelper.ServerPath(options, path);
            using var verifier = new ServerVerificationHelper(_fixture.ServerAddress, _fixture.MappedSensorPort, _fixture.AccessKey);
            var found = await verifier.WaitForValueAsync(serverPath, 1, CollectorOptionsHelper.VerificationTimeout);
            Assert.True(found, $"Rate sensor value not received at path: {serverPath}");

            await collector.Stop();
        }


        [Fact]
        public async Task SendIntBarValue_ServerReceivesCorrectData()
        {
            var path = CollectorOptionsHelper.UniqueSensorPath("intbar_sensor");
            var options = _fixture.CreateCollectorOptions();
            using var collector = new DataCollector(options);
            await collector.Start();

            var sensor = collector.CreateIntBarSensor(path, barPeriod: 1000, postPeriod: 1000);
            sensor.AddValue(10);
            sensor.AddValue(20);
            sensor.AddValue(30);

            var serverPath = CollectorOptionsHelper.ServerPath(options, path);
            using var verifier = new ServerVerificationHelper(_fixture.ServerAddress, _fixture.MappedSensorPort, _fixture.AccessKey);
            var bars = await verifier.WaitForAndGetAllBarValuesAsync(serverPath, 1, CollectorOptionsHelper.VerificationTimeout);
            Assert.Single(bars);
            Assert.Equal(10.ToString(), bars[0].Min);
            Assert.Equal(30.ToString(), bars[0].Max);
            Assert.Equal(20.ToString(), bars[0].Mean);

            await collector.Stop();
        }


        [Fact]
        public async Task SendDoubleBarValue_ServerReceivesCorrectData()
        {
            var path = CollectorOptionsHelper.UniqueSensorPath("doublebar_sensor");
            var options = _fixture.CreateCollectorOptions();
            using var collector = new DataCollector(options);
            await collector.Start();

            var sensor = collector.CreateDoubleBarSensor(path, barPeriod: 1000, postPeriod: 1000);
            sensor.AddValue(1.5);
            sensor.AddValue(2.5);
            sensor.AddValue(3.5);

            var serverPath = CollectorOptionsHelper.ServerPath(options, path);
            using var verifier = new ServerVerificationHelper(_fixture.ServerAddress, _fixture.MappedSensorPort, _fixture.AccessKey);
            var bars = await verifier.WaitForAndGetAllBarValuesAsync(serverPath, 1, CollectorOptionsHelper.VerificationTimeout);
            Assert.Single(bars);
            Assert.Equal(1.5.ToString(), bars[0].Min);
            Assert.Equal(3.5.ToString(), bars[0].Max);
            Assert.Equal(2.5.ToString(), bars[0].Mean);

            await collector.Stop();
        }


        [Fact]
        public async Task SendEnumValue_ServerReceivesCorrectData()
        {
            var path = CollectorOptionsHelper.UniqueSensorPath("enum_sensor");
            var options = _fixture.CreateCollectorOptions();
            using var collector = new DataCollector(options);
            await collector.Start();

            var sensor = collector.CreateEnumSensor(path);
            sensor.AddValue(2);

            var serverPath = CollectorOptionsHelper.ServerPath(options, path);
            using var verifier = new ServerVerificationHelper(_fixture.ServerAddress, _fixture.MappedSensorPort, _fixture.AccessKey);
            var values = await verifier.WaitForAndGetAllValuesAsync(serverPath, 1, CollectorOptionsHelper.VerificationTimeout);
            Assert.Single(values);
            Assert.Equal(2.ToString(), values[0]);

            await collector.Stop();
        }


        [Fact]
        public async Task SendFileValue_ServerReceivesCorrectData()
        {
            var path = CollectorOptionsHelper.UniqueSensorPath("file_sensor");
            var options = _fixture.CreateCollectorOptions();
            using var collector = new DataCollector(options);
            await collector.Start();

            var sensor = collector.CreateFileSensor(path, "test_file", "txt");
            sensor.AddValue("file content for integration test");

            var serverPath = CollectorOptionsHelper.ServerPath(options, path);
            using var verifier = new ServerVerificationHelper(_fixture.ServerAddress, _fixture.MappedSensorPort, _fixture.AccessKey);
            var file = await verifier.WaitForFileValueAsync(serverPath, CollectorOptionsHelper.VerificationTimeout);
            Assert.NotNull(file);
            Assert.Equal("test_file", file.FileName);
            Assert.Equal("txt", file.Extension);

            await collector.Stop();
        }
    }
}
