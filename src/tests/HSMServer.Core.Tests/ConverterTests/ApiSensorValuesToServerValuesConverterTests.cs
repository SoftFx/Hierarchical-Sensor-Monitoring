using HSMSensorDataObjects;
using HSMServer.ApiObjectsConverters;
using HSMServer.Core.Tests.Infrastructure;
using Xunit;

namespace HSMServer.Core.Tests.ConverterTests
{
    public class ApiSensorValuesToServerValuesConverterTests : IClassFixture<EntitiesConverterFixture>
    {
        private readonly ApiSensorValuesFactory _apiSensorValuesFactory;


        public ApiSensorValuesToServerValuesConverterTests(EntitiesConverterFixture converterFixture)
        {
            _apiSensorValuesFactory = converterFixture.ApiSensorValuesFactory;
        }


        [Theory]
        [InlineData(SensorType.BooleanSensor)]
        [InlineData(SensorType.IntSensor)]
        [InlineData(SensorType.DoubleSensor)]
        [InlineData(SensorType.StringSensor)]
        [InlineData(SensorType.IntegerBarSensor)]
        [InlineData(SensorType.DoubleBarSensor)]
        [Trait("Category", "Simple")]
        public void ApiSensorValueToServerSensorValueConverterTest(SensorType type)
        {
            var apiSensorValue = _apiSensorValuesFactory.BuildSensorValue(type);

            var baseValue = apiSensorValue.Convert();

            ApiSensorValuesTester.TestServerSensorValue(apiSensorValue, baseValue);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void FileSensorValueToFileValueConverterTest()
        {
            var apiSensorValue = _apiSensorValuesFactory.BuildFileSensorValue();

            var baseValue = apiSensorValue.Convert();

            ApiSensorValuesTester.TestServerSensorValue(apiSensorValue, baseValue);
        }
    }
}
