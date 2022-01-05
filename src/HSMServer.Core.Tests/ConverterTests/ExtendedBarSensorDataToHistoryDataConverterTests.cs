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


        [Theory]
        [InlineData(SensorType.IntegerBarSensor)]
        [InlineData(SensorType.DoubleBarSensor)]
        [Trait("Category", "Simple")]
        public void ExtendedBarSensorDataToHistoryDataConverterTest(SensorType type)
        {
            var sensorData = _sensorValuesFactory.BuildExtendedBarSensorData(type);

            var historyData = sensorData.Convert();

            SensorValuesTester.TestSensorHistoryDataFromExtendedBarSensorData(sensorData, historyData);
        }

        [Theory]
        [InlineData(SensorType.IntegerBarSensor)]
        [InlineData(SensorType.DoubleBarSensor)]
        [Trait("Category", "With min EndTime")]
        public void ExtendedBarSensorDataToHistoryDataConverter_WithMinEndTime_Test(SensorType type)
        {
            DateTime sensorValueEndTime = DateTime.MinValue;

            var sensorData = _sensorValuesFactory.BuildExtendedBarSensorData(type);
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
