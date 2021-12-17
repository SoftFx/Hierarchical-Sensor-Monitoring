using System;
using System.Text.Json;
using HSMSensorDataObjects;
using HSMServer.Core.Model.Sensor;
using HSMServer.Core.MonitoringServerCore;
using HSMServer.Core.Tests.Infrastructure;
using Xunit;

namespace HSMServer.Core.Tests.ConverterTests
{
    public class ExtendedBarSensorDataToHistoryDataConverterTests : IClassFixture<EntitiesConverterFixture>
    {
        private readonly SensorValuesFactory _sensorValuesFactory;
        private readonly Converter _converter;


        public ExtendedBarSensorDataToHistoryDataConverterTests(EntitiesConverterFixture fixture)
        {
            _sensorValuesFactory = fixture.SensorValuesFactory;
            _converter = new Converter(CommonMoqs.CreateNullLogger<Converter>());
        }


        [Fact]
        [Trait("Category", "Simple")]
        public void ExtendedIntBarSensorDataToHistoryDataConverterTest()
        {
            var sensorData = _sensorValuesFactory.BuildExtendedIntBarSensorData();

            var historyData = _converter.Convert(sensorData);

            SensorValuesTester.TestSensorHistoryDataFromExtendedBarSensorData(sensorData, historyData);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void ExtendedDoubleBarSensorDataToHistoryDataConverterTest()
        {
            var sensorData = _sensorValuesFactory.BuildExtendedDoubleBarSensorData();

            var historyData = _converter.Convert(sensorData);

            SensorValuesTester.TestSensorHistoryDataFromExtendedBarSensorData(sensorData, historyData);
        }


        [Fact]
        [Trait("Category", "With min EndTime")]
        public void ExtendedIntBarSensorDataToHistoryDataConverter_WithMinEndTime_Test()
        {
            DateTime sensorValueEndTime = DateTime.MinValue;

            var sensorData = _sensorValuesFactory.BuildExtendedIntBarSensorData();
            sensorData.Value.EndTime = sensorValueEndTime;

            var historyData = _converter.Convert(sensorData);

            Assert.DoesNotContain(JsonSerializer.Serialize(sensorValueEndTime), historyData.TypedData);
        }

        [Fact]
        [Trait("Category", "With min EndTime")]
        public void ExtendedDoubleBarSensorDataToHistoryDataConverter_WithMinEndTime_Test()
        {
            DateTime sensorValueEndTime = DateTime.MinValue;

            var sensorData = _sensorValuesFactory.BuildExtendedDoubleBarSensorData();
            sensorData.Value.EndTime = sensorValueEndTime;

            var historyData = _converter.Convert(sensorData);

            Assert.DoesNotContain(JsonSerializer.Serialize(sensorValueEndTime), historyData.TypedData);
        }


        [Fact]
        [Trait("Category", "Not Bar ValueType")]
        public void ExtendedDoubleBarSensorDataToHistoryDataConverter_NotBarValueType_Test()
        {
            var sensorData = _sensorValuesFactory.BuildExtendedDoubleBarSensorData();
            sensorData.ValueType = SensorType.BooleanSensor;

            var historyData = _converter.Convert(sensorData);

            Assert.Null(historyData);
        }


        [Fact]
        [Trait("Category", "Error")]
        public void ExtendedIntBarSensorDataToHistoryDataConverter_Error_Test()
        {
            var sensorData = _sensorValuesFactory.BuildExtendedIntBarSensorData();
            sensorData.ValueType = SensorType.DoubleBarSensor;

            var historyData = _converter.Convert(sensorData);

            TestDefaultSensorHistoryData(historyData);
        }

        [Fact]
        [Trait("Category", "Error")]
        public void ExtendedDoubleBarSensorDataToHistoryDataConverter_Error_Test()
        {
            var sensorData = _sensorValuesFactory.BuildExtendedDoubleBarSensorData();
            sensorData.ValueType = SensorType.IntegerBarSensor;

            var historyData = _converter.Convert(sensorData);

            TestDefaultSensorHistoryData(historyData);
        }


        private static void TestDefaultSensorHistoryData(SensorHistoryData historyData)
        {
            Assert.NotNull(historyData);
            Assert.Null(historyData.TypedData);
            Assert.Equal((SensorType)0, historyData.SensorType);
            Assert.Equal(DateTime.MinValue, historyData.Time);
        }
    }
}
