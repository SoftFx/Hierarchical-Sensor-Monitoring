using HSMSensorDataObjects;
using HSMServer.ApiObjectsConverters;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Services;
using Xunit;

namespace HSMServer.Core.Tests.ConverterTests
{
    public class ApiSensorValuesToServerValuesConverterTests : IClassFixture<EntitiesConverterFixture>
    {
        private readonly ApiSensorValuesFactory _apiSensorValuesFactory;
        private readonly IHtmlSanitizerService _sanitizer;

        public ApiSensorValuesToServerValuesConverterTests(EntitiesConverterFixture converterFixture)
        {
            _apiSensorValuesFactory = converterFixture.ApiSensorValuesFactory;
            _sanitizer = new HtmlSanitizerService();
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

            var baseValue = apiSensorValue.Convert(_sanitizer);

            ApiSensorValuesTester.TestServerSensorValue(apiSensorValue, baseValue);
        }

        [Fact]
        [Trait("Category", "Simple")]
        public void FileSensorValueToFileValueConverterTest()
        {
            var apiSensorValue = _apiSensorValuesFactory.BuildFileSensorValue();

            var baseValue = apiSensorValue.Convert(_sanitizer);

            ApiSensorValuesTester.TestServerSensorValue(apiSensorValue, baseValue);
        }
    }
}
