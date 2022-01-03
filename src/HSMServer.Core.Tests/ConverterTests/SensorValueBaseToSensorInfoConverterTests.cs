using System.Collections.Generic;
using HSMSensorDataObjects;
using HSMServer.Core.Converters;
using HSMServer.Core.Tests.Infrastructure;
using Xunit;

namespace HSMServer.Core.Tests.ConverterTests
{
    public class SensorValueBaseToSensorInfoConverterTests : IClassFixture<EntitiesConverterFixture>
    {
        private const char SensorPathSeparator = '/';

        private readonly string _productName = EntitiesConverterFixture.ProductKey;

        private readonly SensorValuesFactory _sensorValuesFactory;
        private readonly SensorValuesTester _sensorValuesTester;


        public SensorValueBaseToSensorInfoConverterTests(EntitiesConverterFixture fixture)
        {
            _sensorValuesFactory = fixture.SensorValuesFactory;
            _sensorValuesTester = fixture.SensorValuesTester;
        }


        [Theory]
        [InlineData(SensorType.BooleanSensor)]
        [InlineData(SensorType.IntSensor)]
        [InlineData(SensorType.DoubleSensor)]
        [InlineData(SensorType.StringSensor)]
        [InlineData(SensorType.IntegerBarSensor)]
        [InlineData(SensorType.DoubleBarSensor)]
        [InlineData(SensorType.FileSensorBytes)]
        [InlineData(SensorType.FileSensor)]
        [Trait("Category", "Simple")]
        public void SensorValueToSensorInfoConverterTest(SensorType type)
        {
            var sensorValue = _sensorValuesFactory.BuildSensorValue(type);

            var sensorInfo = sensorValue.Convert(_productName);

            _sensorValuesTester.TestSensorInfoFromDB(sensorValue, sensorInfo);
        }

        [Fact]
        [Trait("Category", "With complex path")]
        public void FileSensorValueToSensorInfoConverter_WithComplexPath_Test()
        {
            var sensorValue = _sensorValuesFactory.BuildFileSensorValue();
            sensorValue.Path = GetSensorPath(_productName, sensorValue.Path);

            var sensorInfo = sensorValue.Convert(_productName);

            Assert.Equal(GetSensorName(sensorValue.Path), sensorInfo.SensorName);
        }


        private static string GetSensorPath(string productName, string sensorName) =>
            string.Join(SensorPathSeparator, new List<string> { productName, sensorName });

        private static string GetSensorName(string path) => path.Split(SensorPathSeparator)?[^1];
    }
}
