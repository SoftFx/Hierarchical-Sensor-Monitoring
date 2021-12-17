using System;
using System.Text.Json;
using HSMSensorDataObjects;
using HSMServer.Core.Converters;
using HSMServer.Core.Tests.Infrastructure;
using Xunit;

namespace HSMServer.Core.Tests.ConverterTests
{
    public class ExtendedBarSensorDataToHistoryDataConverterTests : IClassFixture<EntitiesConverterFixture>
    {
        private readonly SensorValuesFactory _sensorValuesFactory;


        public ExtendedBarSensorDataToHistoryDataConverterTests(EntitiesConverterFixture fixture) =>
            _sensorValuesFactory = fixture.SensorValuesFactory;


        [Fact]
        [Trait("Category", "Simple")]
        public void ExtendedIntBarSensorDataToHistoryDataConverterTest()
        {
            var sensorData = _sensorValuesFactory.BuildExtendedIntBarSensorData();

            var historyData = sensorData.Convert();

            SensorValuesTester.TestSensorHistoryDataFromExtendedBarSensorData(sensorData, historyData);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void ExtendedDoubleBarSensorDataToHistoryDataConverterTest()
        {
            var sensorData = _sensorValuesFactory.BuildExtendedDoubleBarSensorData();

            var historyData = sensorData.Convert();

            SensorValuesTester.TestSensorHistoryDataFromExtendedBarSensorData(sensorData, historyData);
        }


        [Fact]
        [Trait("Category", "With min EndTime")]
        public void ExtendedIntBarSensorDataToHistoryDataConverter_WithMinEndTime_Test()
        {
            DateTime sensorValueEndTime = DateTime.MinValue;

            var sensorData = _sensorValuesFactory.BuildExtendedIntBarSensorData();
            sensorData.Value.EndTime = sensorValueEndTime;

            var historyData = sensorData.Convert();

            Assert.DoesNotContain(JsonSerializer.Serialize(sensorValueEndTime), historyData.TypedData);
        }

        [Fact]
        [Trait("Category", "With min EndTime")]
        public void ExtendedDoubleBarSensorDataToHistoryDataConverter_WithMinEndTime_Test()
        {
            DateTime sensorValueEndTime = DateTime.MinValue;

            var sensorData = _sensorValuesFactory.BuildExtendedDoubleBarSensorData();
            sensorData.Value.EndTime = sensorValueEndTime;

            var historyData = sensorData.Convert();

            Assert.DoesNotContain(JsonSerializer.Serialize(sensorValueEndTime), historyData.TypedData);
        }


        [Fact]
        [Trait("Category", "Not Bar ValueType")]
        public void ExtendedDoubleBarSensorDataToHistoryDataConverter_NotBarValueType_Test()
        {
            var sensorData = _sensorValuesFactory.BuildExtendedDoubleBarSensorData();
            sensorData.ValueType = SensorType.BooleanSensor;

            var historyData = sensorData.Convert();

            Assert.Null(historyData);
        }


        [Fact]
        [Trait("Category", "Error")]
        public void ExtendedIntBarSensorDataToHistoryDataConverter_Error_Test()
        {
            var sensorData = _sensorValuesFactory.BuildExtendedIntBarSensorData();
            sensorData.ValueType = SensorType.DoubleBarSensor;

            var historyData = sensorData.Convert();

            Assert.Equal(SensorType.IntegerBarSensor, historyData.SensorType);
            SensorValuesTester.TestSensorHistoryDataFromDB(sensorData.Value, historyData);
        }

        [Fact]
        [Trait("Category", "Error")]
        public void ExtendedDoubleBarSensorDataToHistoryDataConverter_Error_Test()
        {
            var sensorData = _sensorValuesFactory.BuildExtendedDoubleBarSensorData();
            sensorData.ValueType = SensorType.IntegerBarSensor;

            var historyData = sensorData.Convert();

            Assert.Equal(SensorType.DoubleBarSensor, historyData.SensorType);
            SensorValuesTester.TestSensorHistoryDataFromDB(sensorData.Value, historyData);
        }
    }
}
