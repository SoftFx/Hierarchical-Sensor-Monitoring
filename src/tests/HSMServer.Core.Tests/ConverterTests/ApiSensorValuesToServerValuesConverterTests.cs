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


        [Theory]
        [InlineData(SensorType.BooleanSensor)]
        [InlineData(SensorType.IntSensor)]
        [InlineData(SensorType.DoubleSensor)]
        [InlineData(SensorType.StringSensor)]
        [InlineData(SensorType.IntegerBarSensor)]
        [InlineData(SensorType.DoubleBarSensor)]
        [Trait("Category", "UnitedSensorValue")]
        public void UnitedSensorValueToServerSensorValueConverterTest(SensorType type)
        {
            var apiSensorValue = _apiSensorValuesFactory.BuildUnitedSensorValue(type);

            Model.BaseValue baseValue = type switch
            {
                SensorType.BooleanSensor => apiSensorValue.ConvertToBool(),
                SensorType.IntSensor => apiSensorValue.ConvertToInt(),
                SensorType.DoubleSensor => apiSensorValue.ConvertToDouble(),
                SensorType.StringSensor => apiSensorValue.ConvertToString(),
                SensorType.IntegerBarSensor => apiSensorValue.ConvertToIntBar(),
                SensorType.DoubleBarSensor => apiSensorValue.ConvertToDoubleBar(),
                _ => null,
            };

            ApiSensorValuesTester.TestServerSensorValue(apiSensorValue, baseValue);
        }
    }
}
